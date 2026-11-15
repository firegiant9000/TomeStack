using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// C2-3 on the bundled SRD content (srd-&lt;family&gt;-monk.json): a Monk of each family, levels 1-20, side by side.
/// Values are the SRD class tables' (docs/licensing/srd-pack-review.md, "C2-3").
/// </summary>
public class SrdMonkTests
{
    private static readonly string[] Families = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static string Prefix(string family) => family == RulesFamilies.Srd51 ? "51" : "52";

    private static string PackName(string family) => family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1";

    private static ContentReference Ref(string family, long content, long? revision = null) =>
        new(Guid.Parse($"{Prefix(family)}c00000-0000-4000-8000-{content:D12}"), Guid.Parse($"{Prefix(family)}e00000-0000-4000-8000-{revision ?? content:D12}"));

    private static ContentPack MonkPack(string family) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{PackName(family)}-monk.json");

    private static ContentPack Pack(string family, string name) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{PackName(family)}-{name}.json");

    private static ContentReference MonkClass(string family) => Ref(family, 420001);

    private static ContentReference OpenHand(string family) => Ref(family, 420002);

    // C2-6: the 5.1 Monk as srd-5.1-monk-v12.json revised it.
    private static ContentReference NewClass51 => Ref(RulesFamilies.Srd51, 420001, 12420001);

    private static ContentReference V12(long content, string family = RulesFamilies.Srd51) => Ref(family, content, 12000000 + (content % 1000000));

    private static ContentPack V12Pack51 => TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-monk-v12.json");

    private static ContentPack V12Pack521 => TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-monk-v12.json");

    private static ContentPack V12Pack(string family) => family == RulesFamilies.Srd51 ? V12Pack51 : V12Pack521;

    private static ContentReference NewClass(string family) => Ref(family, 420001, 12420001);

    private static Character V12Monk(int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, string family = RulesFamilies.Srd51) =>
        Monk(family, level, equipment, scores, NewClass(family));

    private static Character V12Monk52(int level, IReadOnlyList<EquipmentEntry>? equipment = null) => V12Monk(level, equipment, null, RulesFamilies.Srd521);

    /// <summary>Monk's Focus (5.2.1) / Ki (5.1): the feature that declares the pool every other spender names.</summary>
    private static ContentReference PoolFeature(string family) => Ref(family, 420103);

    private static string PoolId(string family) => family == RulesFamilies.Srd51 ? "ki" : "focus";

    private static string SubclassChoice(string family) => family == RulesFamilies.Srd51 ? "monastic-tradition" : "monk-subclass";

    private static ContentReference Skill(string family, string name) => MonkPack(family).Revisions.Single(r => r.Name == $"Monk Skill: {name}").Reference;

    private static ContentReference Weapon(string family, string name) => Pack(family, "equipment").Revisions.Last(r => r.Name == name).Reference;

    private static ContentReference Armor(string family, string name) => Pack(family, "armor").Revisions.Single(r => r.Name == name).Reference;

    private static string LeatherName(string family) => family == RulesFamilies.Srd51 ? "Leather" : "Leather Armor";

    /// <summary>
    /// A Monk of <paramref name="level"/> with the choices answered: Acrobatics and Stealth, and the Open Hand from level 3.
    /// Str 10 (+0), Dex 16 (+3), Con 14 (+2), Wis 14 (+2).
    /// </summary>
    private static Character Monk(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, ContentReference? cls = null)
    {
        cls ??= MonkClass(family);
        List<ChoiceSelection> choices = [new(cls, "monk-skills", [Skill(family, "Acrobatics"), Skill(family, "Stealth")])];
        if (level >= 3)
            choices.Add(new(cls, SubclassChoice(family), [OpenHand(family)]));
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD Monk", RulesFamily = family, Level = level,
            Classes = [new(cls, level)],
            BaseAbilities = scores ?? new(10, 16, 14, 10, 14, 8),
            Choices = choices,
            Equipment = equipment ?? [],
        };
    }

    private static readonly string[] Broken = ["choice.unresolved", "content.missing", "effect.invalid-formula", "choice.invalid-option", "attack.die-invalid"];

    private static void AssertClean(CharacterSheet sheet) => Assert.DoesNotContain(sheet.Diagnostics, d => Broken.Contains(d.Code));

    private static ResourceValue Pool(CharacterSheet sheet, string family) => sheet.Resources!.Single(r => r.ResourceId == PoolId(family));

    private static AttackEntry Strike(CharacterSheet sheet, string effectId) =>
        sheet.Attacks!.Single(a => a.Source == AttackSource.Feature && a.EffectId == effectId);

    private static CharacterSheet UseRoll(TempApp temp, string family, Guid id, long content, string rollId)
    {
        var stamp = temp.App.GetCharacter(id).Character.UpdatedAt;
        return temp.App.Play(new(id, PlayActionKind.UseAction, Confirm: true, ContentId: Ref(family, content).ContentId, RollId: rollId, ExpectedUpdatedAt: stamp)).Sheet;
    }

    private static CharacterSheet ToggleOn(TempApp temp, string family, Guid id, long content, string toggleId) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Ref(family, content).ContentId, ToggleId: toggleId)).Sheet;

    private static int MartialArtsDie(string family, int level)
    {
        // SRD class tables: 5.2.1 d6 / d8 / d10 / d12, 5.1 d4 / d6 / d8 / d10, at levels 1, 5, 11 and 17.
        var step = level >= 17 ? 3 : level >= 11 ? 2 : level >= 5 ? 1 : 0;
        return (family == RulesFamilies.Srd51 ? 4 : 6) + (2 * step);
    }

    private static int UnarmoredMovement(int level) => level switch
    {
        < 2 => 0,
        < 6 => 10,
        < 10 => 15,
        < 14 => 20,
        < 18 => 25,
        _ => 30,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void Calculates_levels_1_to_20_side_by_side(int level)
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Monk(family, level)).Sheet;

            // The Martial Arts die follows the family's table; Dex +3 is the damage bonus.
            var row = Strike(sheet, "martial-arts-strike");
            Assert.Equal($"1d{MartialArtsDie(family, level)}+3", row.Damage);
            Assert.True(row.Available, $"{family} L{level}");
            Assert.Equal(MartialArtsDie(family, level), sheet.Scales!.Single(s => s.ScaleId == "martialArts").Value);

            // Focus / Ki points: none at level 1, then the Monk level.
            if (level == 1)
                Assert.DoesNotContain(sheet.Resources ?? [], r => r.ResourceId == PoolId(family));
            else
                Assert.Equal(level, Pool(sheet, family).Maximum);

            var speed = sheet.Field(FieldIds.Speed);
            Assert.Equal(30 + UnarmoredMovement(level), speed.Value);
            Assert.NotEmpty(speed.Trace);

            // Unarmored Defense: 10 + Dex 3 + Wis 2 (Body and Mind at 20 is reference, so it never changes the scores).
            var armorClass = sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(15, armorClass.Value);
            Assert.NotEmpty(armorClass.Trace);

            // d8: 8 + Con 2 at level 1, then 5 + 2 per level (SRD fixed value).
            Assert.Equal(10 + (7 * (level - 1)), sheet.Field(FieldIds.HitPoints).Value);
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Save(Ability.Str)).Mark);
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Save(Ability.Dex)).Mark);
            AssertClean(sheet);
            Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{family} {c.ChoiceId}"));
        }
    }

    [Fact]
    public void Martial_Arts_uses_the_better_of_strength_and_dexterity()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var dex = temp.App.SaveCharacter(Monk(family, 1)).Sheet;
            var dexRow = Strike(dex, "martial-arts-strike");
            Assert.Equal((Ability.Dex, 3 + 2, true), (dexRow.Ability, dexRow.ToHit, dexRow.Proficient)); // Dex +3, proficiency bonus +2

            var str = temp.App.SaveCharacter(Monk(family, 1, scores: new(18, 12, 14, 10, 14, 8))).Sheet;
            var strRow = Strike(str, "martial-arts-strike");
            Assert.Equal((Ability.Str, 4 + 2), (strRow.Ability, strRow.ToHit)); // Str +4 beats Dex +1
            Assert.Equal($"1d{MartialArtsDie(family, 1)}+4", strRow.Damage);

            // The second row is the bonus-action one.
            Assert.Equal(Activation.BonusAction, Strike(dex, "martial-arts-bonus-strike").Activation);
            Assert.Null(dexRow.Activation);
        }
    }

    [Fact]
    public void Unarmored_Defense_applies_without_armor_and_yields_to_worn_armor()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var bare = temp.App.SaveCharacter(Monk(family, 1)).Sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(15, bare.Value); // 10 + Dex 3 + Wis 2
            Assert.DoesNotContain(bare.Warnings, w => w.Code == "equipment.armor-untrained");

            var leather = Armor(family, LeatherName(family));
            var worn = temp.App.SaveCharacter(Monk(family, 1, [new(leather, Equipped: true)])).Sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(11 + 3, worn.Value); // the armor's own AC (11 + Dex): Unarmored Defense does not apply
            // The Monk grants armor.none, so the untrained-armor check runs.
            Assert.Contains(worn.Warnings, w => w.Code == "equipment.armor-untrained");
        }
    }

    [Fact]
    public void Unarmored_Defense_does_not_check_a_shield()
    {
        // G4: TomeStack has no shield condition. With a Shield the replace still applies and the shield adds its +2; the feature's
        // own text tells the player to adjust by hand.
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var shield = Armor(family, "Shield");
            var sheet = temp.App.SaveCharacter(Monk(family, 1, [new(shield, Equipped: true)])).Sheet;

            // The Monk has no shield training, and the families differ on an untrained shield (UntrainedShieldGivesArmorClass is
            // true for 5.1, false for 5.2.1): 5.1 still adds it (15 + 2 = 17), 5.2.1 adds nothing (15). Either way the replace is
            // not withdrawn, and the untrained-shield warning is raised (its wording differs).
            var ac = sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(family == RulesFamilies.Srd51 ? 17 : 15, ac.Value);
            Assert.Contains(ac.Warnings, w => w.Code == "equipment.shield-untrained");
            Assert.DoesNotContain(ac.Warnings, w => w.Code == "equipment.armor-untrained");
            var feature = sheet.Features!.Single(f => f.Name == "Unarmored Defense");
            Assert.Contains("does not check shield", string.Join(" ", feature.Effects.Select(e => e.Text)), StringComparison.OrdinalIgnoreCase);
        }

        // The C2-6 5.1 revision (G4) checks it: with a shield the replace switches off and the plain 10 + Dex applies.
        // Wis 18 (+4) makes the replace visible: bare 10 + 3 + 4 = 17; with the shield 10 + 3 + the shield's 2 = 15.
        var wise = new AbilityScores(10, 16, 14, 10, 18, 8);
        var v12Shield = Armor(RulesFamilies.Srd51, "Shield");
        Assert.Equal(17, temp.App.SaveCharacter(V12Monk(1, scores: wise)).Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(19, temp.App.SaveCharacter(Monk(RulesFamilies.Srd51, 1, [new(v12Shield, Equipped: true)], wise)).Sheet.Field(FieldIds.ArmorClass).Value); // old revision: replace kept, shield added
        Assert.Equal(15, temp.App.SaveCharacter(V12Monk(1, [new(v12Shield, Equipped: true)], wise)).Sheet.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void A_barbarian_monk_takes_the_higher_unarmored_defense()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var barbarian = family == RulesFamilies.Srd51 ? Ref(family, 3, 409003) : Ref(family, 11, 409011);
            var skills = Enumerable.Range(family == RulesFamilies.Srd51 ? 8 : 17, 6).Take(2).Select(n => Ref(family, n));

            CharacterSheet Build(AbilityScores scores) => temp.App.SaveCharacter(new Character
            {
                Id = Guid.NewGuid(), Name = "Test Barbarian Monk", RulesFamily = family, Level = 2,
                Classes = [new(barbarian, 1), new(MonkClass(family), 1)],
                BaseAbilities = scores,
                Choices = [new(barbarian, "barbarian-skills", [.. skills])],
            }).Sheet;

            // Str 13 Dex 16 Con 16 Wis 14 (+3): the Barbarian's 10 + Dex 3 + Con 3 = 16 beats the Monk's 10 + 3 + 2 = 15.
            var strong = Build(new(13, 16, 16, 8, 14, 8));
            Assert.Equal(16, strong.Field(FieldIds.ArmorClass).Value);
            Assert.DoesNotContain(strong.Diagnostics, d => d.Code.StartsWith("restriction.", StringComparison.Ordinal));

            // Con 14 (+2) Wis 16 (+3): the Monk's 10 + 3 + 3 = 16 beats the Barbarian's 10 + 3 + 2 = 15.
            var wise = Build(new(13, 16, 14, 8, 16, 8));
            Assert.Equal(16, wise.Field(FieldIds.ArmorClass).Value);
            Assert.DoesNotContain(wise.Diagnostics, d => d.Code.StartsWith("restriction.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Focus_spends_and_a_short_rest_restores()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var id = temp.App.SaveCharacter(Monk(family, 5)).Character.Id;
            var full = Pool(temp.App.GetCharacter(id).Sheet, family);
            Assert.Equal((5, 5), (full.Maximum, full.Current));
            Assert.Contains(full.Recoveries, r => r.On == RestPeriod.ShortRest);

            var used = UseRoll(temp, family, id, 420103, "flurryOfBlows");
            Assert.Equal(4, Pool(used, family).Current);

            var preview = temp.App.PreviewRest(id, RestPeriod.ShortRest);
            var rested = temp.App.Rest(new(id, RestPeriod.ShortRest, Confirm: true, Basis: preview.Basis)).Sheet;
            Assert.Equal(5, Pool(rested, family).Current);
        }
    }

    public static TheoryData<string, long, string, int, bool> Spends()
    {
        var data = new TheoryData<string, long, string, int, bool>
        {
            // family, content, roll or toggle id, cost, toggle
            { RulesFamilies.Srd521, 420106, "deflectAttacksRedirect", 1, false },
            { RulesFamilies.Srd521, 420110, "stunningStrike", 1, false },
            { RulesFamilies.Srd521, 420117, "disciplinedSurvivor", 1, false },
            { RulesFamilies.Srd521, 420504, "quiveringPalmStart", 4, false },
            { RulesFamilies.Srd521, 420119, "superiorDefense", 3, true },
            { RulesFamilies.Srd51, 420105, "deflectMissilesThrow", 1, false },
            { RulesFamilies.Srd51, 420109, "stunningStrike", 1, false },
            { RulesFamilies.Srd51, 420115, "diamondSoul", 1, false },
            { RulesFamilies.Srd51, 420504, "quiveringPalmStart", 3, false },
            { RulesFamilies.Srd51, 420117, "emptyBody", 4, true },
            { RulesFamilies.Srd51, 420117, "astralProjection", 8, false },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Spends))]
    public void Every_cross_feature_spend_draws_on_the_pool(string family, long content, string id, int cost, bool toggle)
    {
        using var temp = new TempApp();
        var characterId = temp.App.SaveCharacter(Monk(family, 20)).Character.Id;
        Assert.Equal(20, Pool(temp.App.GetCharacter(characterId).Sheet, family).Current);

        var after = toggle ? ToggleOn(temp, family, characterId, content, id) : UseRoll(temp, family, characterId, content, id);

        Assert.Equal(20 - cost, Pool(after, family).Current);
    }

    [Fact]
    public void Stunning_Strike_DC_is_8_plus_wis_plus_pb()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Monk(family, 5)).Sheet;

            var stunning = sheet.Features!.Single(f => f.Name == "Stunning Strike").Effects.Single(e => e.Type == "roll");
            Assert.Equal((Ability.Con, 8 + 2 + 3), (stunning.SaveAbility, stunning.SaveDc)); // Wis +2, proficiency bonus +3
            Assert.NotEmpty(stunning.SaveTrace!);
        }
    }

    [Fact]
    public void Weapon_proficiencies_follow_each_family()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var shortsword = Weapon(family, "Shortsword");
            var staff = Weapon(family, "Quarterstaff");
            // 5.2.1: Simple weapons and Martial weapons with the Light property (no Longsword). 5.1: Simple weapons and shortswords (no Scimitar).
            var other = Weapon(family, family == RulesFamilies.Srd51 ? "Scimitar" : "Longsword");
            var sheet = temp.App.SaveCharacter(Monk(family, 1, [new(shortsword, Equipped: true), new(staff, Equipped: true), new(other, Equipped: true)])).Sheet;

            bool Proficient(ContentReference item) => sheet.Attacks!.Single(a => a.Item == item).Proficient;
            Assert.True(Proficient(shortsword), $"{family} shortsword");
            Assert.True(Proficient(staff), $"{family} quarterstaff");
            Assert.False(Proficient(other), $"{family} other");
        }
    }

    [Fact]
    public void A_multiclass_monk_gets_what_its_family_gives()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var wizard = Pack(family, "classes").Revisions.Last(r => r.Name == "Wizard" && r.Kind == ContentKind.Class).Reference;
            var shortsword = Weapon(family, "Shortsword");
            var character = new Character
            {
                Id = Guid.NewGuid(), Name = "Test Wizard Monk", RulesFamily = family, Level = 2,
                Classes = [new(wizard, 1), new(MonkClass(family), 1)],
                BaseAbilities = new(10, 13, 12, 13, 13, 8),
                Equipment = [new(shortsword, Equipped: true)],
            };

            var sheet = temp.App.SaveCharacter(character).Sheet;

            // Neither family's Monk gives skills or saves to a multiclass Monk.
            Assert.DoesNotContain(sheet.Choices!, c => c.ChoiceId == "monk-skills");
            Assert.Equal(ProficiencyMark.None, sheet.Field(FieldIds.Save(Ability.Str)).Mark);
            Assert.Equal(ProficiencyMark.None, sheet.Field(FieldIds.Save(Ability.Dex)).Mark);
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
            // 5.2.1 multiclassing gives the Hit Point Die only; 5.1 gives Simple weapons and shortswords.
            Assert.Equal(family == RulesFamilies.Srd51, sheet.Attacks!.Single(a => a.Item == shortsword).Proficient);
        }
    }

    [Fact]
    public void The_multiclass_prerequisite_needs_dex_and_wis()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var wizard = Pack(family, "classes").Revisions.Last(r => r.Name == "Wizard" && r.Kind == ContentKind.Class).Reference;
            CharacterSheet Build(AbilityScores scores) => temp.App.SaveCharacter(new Character
            {
                Id = Guid.NewGuid(), Name = "Test Wizard Monk", RulesFamily = family, Level = 2,
                Classes = [new(wizard, 1), new(MonkClass(family), 1)],
                BaseAbilities = scores,
            }).Sheet;

            Assert.DoesNotContain(Build(new(10, 13, 12, 13, 13, 8)).Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == MonkClass(family));
            Assert.Contains(Build(new(10, 13, 12, 13, 12, 8)).Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == MonkClass(family)); // Wis 12
            Assert.Contains(Build(new(10, 12, 12, 13, 13, 8)).Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == MonkClass(family)); // Dex 12
        }
    }

    [Fact]
    public void Disciplined_Survivor_and_Diamond_Soul_give_every_save_at_14()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var thirteen = temp.App.SaveCharacter(Monk(family, 13)).Sheet;
            Assert.Equal(ProficiencyMark.None, thirteen.Field(FieldIds.Save(Ability.Int)).Mark);

            var fourteen = temp.App.SaveCharacter(Monk(family, 14)).Sheet;
            foreach (var ability in Enum.GetValues<Ability>())
                Assert.Equal(ProficiencyMark.Proficient, fourteen.Field(FieldIds.Save(ability)).Mark);
        }
    }

    [Fact]
    public void Superior_Defense_and_Empty_Body_resist_everything_but_force()
    {
        using var temp = new TempApp();
        foreach (var (family, content, toggle, cost) in new[] { (RulesFamilies.Srd521, 420119L, "superiorDefense", 3), (RulesFamilies.Srd51, 420117L, "emptyBody", 4) })
        {
            var id = temp.App.SaveCharacter(Monk(family, 18)).Character.Id;
            var off = temp.App.GetCharacter(id).Sheet;
            Assert.DoesNotContain(off.DamageDefences ?? [], d => d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(18, Pool(off, family).Current);

            var on = ToggleOn(temp, family, id, content, toggle);

            Assert.Equal(18 - cost, Pool(on, family).Current);
            Assert.Contains(on.DamageDefences!, d => d.Type == "fire" && d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(12, on.DamageDefences!.Count(d => d.Kind == DamageDefenceKind.Resistance));
            Assert.DoesNotContain(on.DamageDefences!, d => d.Type == "force");
        }
    }

    [Fact]
    public void Purity_of_Body_is_poison_immunity_in_5_1()
    {
        using var temp = new TempApp();
        var old = temp.App.SaveCharacter(Monk(RulesFamilies.Srd51, 10)).Sheet;
        Assert.Contains(old.DamageDefences!, d => d.Type == "poison" && d.Kind == DamageDefenceKind.Immunity);
        Assert.DoesNotContain(temp.App.SaveCharacter(Monk(RulesFamilies.Srd51, 9)).Sheet.DamageDefences ?? [], d => d.Type == "poison");

        var modern = temp.App.SaveCharacter(Monk(RulesFamilies.Srd521, 10)).Sheet;
        Assert.DoesNotContain(modern.DamageDefences ?? [], d => d.Kind == DamageDefenceKind.Immunity);
    }

    [Fact]
    public void Body_and_mind_raises_dex_and_wis_to_cap_25()
    {
        using var temp = new TempApp();
        var effects = V12Pack521.Revisions.Single(r => r.Name == "Body and Mind").Effects.OfType<ModifierEffect>().ToList();
        Assert.Equal(2, effects.Count);
        Assert.All(effects, e => Assert.Equal(25, e.Cap));

        var sheet = temp.App.SaveCharacter(V12Monk52(20)).Sheet;
        Assert.Equal(20, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(18, sheet.Field(FieldIds.Score(Ability.Wis)).Value);
        Assert.NotEqual(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Body and Mind").Automation);

        // The cap stops the increase at 25.
        var high = temp.App.SaveCharacter(V12Monk(20, null, new(10, 23, 14, 8, 24, 8), RulesFamilies.Srd521)).Sheet;
        Assert.Equal(25, high.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(25, high.Field(FieldIds.Score(Ability.Wis)).Value);
    }

    [Fact]
    public void Reference_features_are_listed_as_reference()
    {
        using var temp = new TempApp();
        string[] both = ["Ability Score Improvement", "Open Hand Technique"];
        var names = new Dictionary<string, string[]>
        {
            // Both Monks are the C2-6 v12 revisions: Slow Fall, Evasion, Deflect Energy and Body and Mind are Automatic now.
            [RulesFamilies.Srd521] = [.. both, "Empowered Strikes", "Perfect Focus", "Acrobatic Movement", "Heightened Focus", "Self-Restoration", "Epic Boon", "Fleet Step"],
            [RulesFamilies.Srd51] = [.. both, "Ki-Empowered Strikes", "Perfect Self", "Stillness of Mind", "Tongue of the Sun and Moon", "Timeless Body", "Tranquility"],
        };
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(V12Monk(20, null, null, family)).Sheet;
            // Scoped to this family's Monk revisions: other classes have an "Evasion" or a "Slow Fall" of their own.
            var monkRevisions = MonkPack(family).Revisions.Select(r => r.RevisionId).ToHashSet();
            monkRevisions.UnionWith(V12Pack(family).Revisions.Select(r => r.RevisionId));
            var features = sheet.Features!.Where(f => monkRevisions.Contains(f.Content.RevisionId)).ToList();
            foreach (var name in names[family])
            {
                var feature = Assert.Single(features, f => f.Name == name);
                Assert.True(feature.Automation == AutomationStatus.Reference, $"{family}: {name} is {feature.Automation}");
            }
            // The exact set: nothing else in the Monk pack is Reference, so an Automatic feature cannot silently become one.
            var actual = features.Where(f => f.Automation == AutomationStatus.Reference).Select(f => f.Name).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(names[family].Order(StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void The_monk_packs_add_nothing_to_existing_characters()
    {
        string[] monkFiles = ["TomeStack.Content.srd-5.1-monk.json", "TomeStack.Content.srd-5.2.1-monk.json", "TomeStack.Content.srd-5.1-monk-v12.json", "TomeStack.Content.srd-5.2.1-monk-v12.json"];
        var fixtures = new Dictionary<string, string>
        {
            [RulesFamilies.Srd521] = "characters/m1-acceptance-srd521-brenna.json",
            [RulesFamilies.Srd51] = "characters/m1-acceptance-srd51-korga.json",
        };
        InMemoryContentCatalog Catalog(IEnumerable<string> files)
        {
            var packs = files.Select(TomeStackApp.LoadBundledPack).ToList();
            return new(packs.SelectMany(p => p.Sources).DistinctBy(s => s.Id), packs.SelectMany(p => p.Revisions));
        }

        var without = Catalog(TomeStackApp.BundledPacks.Except(monkFiles));
        var with = Catalog(TomeStackApp.BundledPacks);
        foreach (var (family, file) in fixtures)
        {
            var character = TempApp.LoadFixture<Character>(file);
            var before = CharacterCalculator.Calculate(character, without);
            var after = CharacterCalculator.Calculate(character, with);

            Assert.Equal(family, character.RulesFamily);
            Assert.Equal(TempApp.Json(before.Diagnostics), TempApp.Json(after.Diagnostics));
            Assert.Equal(TempApp.Json(before.Choices), TempApp.Json(after.Choices));
            Assert.Equal(TempApp.Json(before.Features), TempApp.Json(after.Features));
        }

        // Nothing outside the Monk packs mentions a Monk id, and no other revision extends a Monk choice.
        var monkRevisions = monkFiles.SelectMany(f => TomeStackApp.LoadBundledPack(f).Revisions).ToList();
        var monkGuids = monkRevisions.SelectMany(r => new[] { r.ContentId, r.RevisionId }).Distinct().Select(g => g.ToString("D")).ToList();
        foreach (var file in TomeStackApp.BundledPacks.Except(monkFiles))
        {
            var pack = TomeStackApp.LoadBundledPack(file);
            // A creation guide (ADR-024) only advises the builder and changes no calculation, so it may name the Monk (guideFor).
            var json = TempApp.Json(pack.Revisions.Where(r => r.Kind != ContentKind.CreationGuide).ToList());
            Assert.DoesNotContain(monkGuids, g => json.Contains(g, StringComparison.OrdinalIgnoreCase));
            var monkContent = monkRevisions.Select(r => r.ContentId).ToHashSet();
            Assert.DoesNotContain(pack.Revisions, r => r.ExtendsChoice is { } e && monkContent.Contains(e.ContentId));
        }
    }

    // ---- C2-6: the 5.1 v12 revisions -------------------------------------------------------------------------------------------

    [Fact]
    public void Unarmored_movement_stops_with_armor_or_shield()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd51;

        Assert.Equal(40, temp.App.SaveCharacter(V12Monk(2)).Sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(50, temp.App.SaveCharacter(V12Monk(10)).Sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(30, temp.App.SaveCharacter(V12Monk(2, [new(Armor(family, "Shield"), Equipped: true)])).Sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(30, temp.App.SaveCharacter(V12Monk(2, [new(Armor(family, "Leather"), Equipped: true)])).Sheet.Field(FieldIds.Speed).Value);

        // The old revision adds the bonus regardless.
        Assert.Equal(40, temp.App.SaveCharacter(Monk(family, 2, [new(Armor(family, "Shield"), Equipped: true)])).Sheet.Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void Slow_fall_reduces_by_five_per_level()
    {
        using var temp = new TempApp();
        var slowFall = V12(420107);
        var id = temp.App.SaveCharacter(V12Monk(4)).Character.Id;

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Slow Fall").Automation);
        var fall = temp.App.PreviewDamage(new(id, 30, null, [new(slowFall.ContentId, "slowFall")], ResponseSource.Fall));
        Assert.Empty(fall.Notes);
        Assert.Equal(20, fall.Reduction);
        Assert.Equal(10, fall.Outcome.Adjusted);

        // Not offered for an attack.
        var attack = temp.App.PreviewDamage(new(id, 30, "slashing", [new(slowFall.ContentId, "slowFall")], ResponseSource.Attack));
        Assert.Equal(30, attack.Outcome.Adjusted);

        // The reaction is spent by hand: no pool.
        Assert.DoesNotContain(temp.App.GetCharacter(id).Sheet.Resources ?? [], r => r.Label == "Reaction");
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_Rogue_and_Monk_multiclass_applies_Evasion_once(string family)
    {
        using var temp = new TempApp();
        var rogueEvasion = Ref(family, 410110, 12410110);
        var monkEvasion = V12(family == RulesFamilies.Srd51 ? 420111 : 420112, family);
        var monk = V12Monk(7, null, null, family);
        var multiclass = monk with
        {
            Level = 14,
            Classes = [new(Ref(family, 410001, 12410001), 7), .. monk.Classes],
        };
        var id = temp.App.SaveCharacter(multiclass).Character.Id;

        // Both Evasions are on the sheet, but the Monk's names the Rogue's as the one it replaces.
        var sheet = temp.App.GetCharacter(id).Sheet;
        Assert.Equal(2, sheet.Features!.Count(f => f.Name == "Evasion"));

        var failure = temp.App.PreviewDamage(new(id, 20, "fire",
            [new(rogueEvasion.ContentId, "evasionFailure"), new(monkEvasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Equal(10, failure.Outcome.Adjusted);
        Assert.Single(failure.Offers, o => o.Response.ResponseId == "evasionFailure");

        var success = temp.App.PreviewDamage(new(id, 20, "fire",
            [new(rogueEvasion.ContentId, "evasionSuccess"), new(monkEvasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        Assert.Equal(0, success.Outcome.Adjusted);
        Assert.Single(success.Offers, o => o.Response.ResponseId == "evasionSuccess");
    }

    [Fact]
    public void Evasion_success_takes_none_failure_takes_half()
    {
        using var temp = new TempApp();
        var evasion = V12(420111);
        var id = temp.App.SaveCharacter(V12Monk(7)).Character.Id;

        var success = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        Assert.Empty(success.Notes);
        Assert.Equal(0, success.Outcome.Adjusted);
        Assert.DoesNotContain(success.Offers, o => o.Response.ResponseId == "evasionFailure");

        var failure = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Empty(failure.Notes);
        Assert.Equal(10, failure.Outcome.Adjusted);
        Assert.DoesNotContain(failure.Offers, o => o.Response.ResponseId == "evasionSuccess");

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Evasion").Automation);
        // 5.1 Evasion has no "unless incapacitated" clause.
        Assert.All(V12Pack51.Revisions.Single(r => r.Name == "Evasion").Effects.OfType<ResponseEffect>(), e => Assert.Null(e.UnlessCondition));
    }

    [Fact]
    public void Deflect_missiles_reduces_by_1d10_plus_dex_plus_level()
    {
        using var temp = new TempApp();
        var deflect = V12(420105);
        var id = temp.App.SaveCharacter(V12Monk(3)).Character.Id;

        // Dex +3 and level 3 on a rolled 6: 12 less.
        var preview = temp.App.PreviewDamage(new(id, 20, "piercing", [new(deflect.ContentId, "deflectMissiles", Rolled: 6)], ResponseSource.Attack));
        Assert.Empty(preview.Notes);
        Assert.Equal(8, preview.Outcome.Adjusted);

        // Taking it is free (the reaction is by hand), and the Ki throw is untouched.
        var feature = temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Deflect Missiles");
        Assert.Equal(AutomationStatus.Automatic, feature.Automation);
        var effects = V12Pack51.Revisions.Single(r => r.Name == "Deflect Missiles").Effects;
        var throwRoll = Assert.Single(effects.OfType<RollEffect>());
        Assert.Equal(("deflectMissilesThrow", "ki", "1"), (throwRoll.RollId, throwRoll.ResourceId, throwRoll.Cost));
        var response = Assert.Single(effects.OfType<ResponseEffect>());
        Assert.Null(response.ResourceId);
    }

    [Fact]
    public void Purity_of_body_grants_poisoned_immunity()
    {
        using var temp = new TempApp();
        Assert.Null(temp.App.SaveCharacter(V12Monk(9)).Sheet.ConditionImmunities);

        var sheet = temp.App.SaveCharacter(V12Monk(10)).Sheet;
        Assert.Equal(["poisoned"], sheet.ConditionImmunities!.Select(c => c.Condition));
        Assert.Equal("Purity of Body", Assert.Single(Assert.Single(sheet.ConditionImmunities!).Sources).Name);
        Assert.Contains(sheet.DamageDefences!, d => d.Kind == DamageDefenceKind.Immunity && string.Equals(d.Type, "Poison", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Purity of Body").Automation);
    }

    [Fact]
    public void The_v12_Monk_keeps_every_unchanged_grant_and_the_Open_Hand()
    {
        var old = MonkPack(RulesFamilies.Srd51).Revisions.Single(r => r.Kind == ContentKind.Class);
        var revised = V12Pack51.Revisions.Single(r => r.Kind == ContentKind.Class);
        var changed = new[] { 420101L, 420104, 420105, 420107, 420111, 420113 };
        IEnumerable<(string, ContentReference)> Grants(ContentRevision r) => r.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => (g.Id, g.Content!));
        var oldGrants = Grants(old).ToDictionary(g => g.Item1, g => g.Item2);
        foreach (var (id, content) in Grants(revised))
            Assert.Equal(changed.Any(n => V12(n).ContentId == content.ContentId) ? V12(int.Parse(content.ContentId.ToString()[^6..])) : oldGrants[id], content);
        Assert.Equal(oldGrants.Count, Grants(revised).Count());
        Assert.Equal(changed.Length, V12Pack51.Revisions.Count(r => r.Kind == ContentKind.Feature));
    }

    [Fact]
    public void A_barbarian_monk_with_a_shield_keeps_the_barbarians_unarmored_defense()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd51;
        var barbarian = Ref(family, 3, 409003);
        var skills = Enumerable.Range(8, 6).Take(2).Select(n => Ref(family, n));
        var shield = Armor(family, "Shield");

        var sheet = temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Barbarian Monk", RulesFamily = family, Level = 2,
            Classes = [new(barbarian, 1), new(NewClass51, 1)],
            BaseAbilities = new(13, 16, 16, 8, 14, 8),
            Choices = [new(barbarian, "barbarian-skills", [.. skills])],
            Equipment = [new(shield, Equipped: true)],
        }).Sheet;

        // The Monk's replace (15) is off with the shield; the Barbarian's 10 + Dex 3 + Con 3 = 16 allows one, plus the shield's 2.
        Assert.Equal(18, sheet.Field(FieldIds.ArmorClass).Value);
    }

    // ---- C2-6: existing characters on the older 5.1 Monk ----------------------------------------------------------------------

    [Fact]
    public void A_C2_3_Monk_calculates_as_before_and_is_offered_the_v12_class()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd51;
        var shield = Armor(family, "Shield");
        var id = temp.App.SaveCharacter(Monk(family, 10, [new(shield, Equipped: true)])).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        AssertClean(sheet);
        Assert.Equal(50, sheet.Field(FieldIds.Speed).Value); // the old speed bonus ignores the shield
        Assert.Null(sheet.ConditionImmunities);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Evasion").Automation);

        var offer = Assert.Single(temp.App.AvailableUpdates(id), o => o.Role == ReferenceRole.Class && o.Name == "Monk");
        Assert.Equal((MonkClass(family), NewClass51, true), (offer.From, offer.To, offer.Bundled));
        Assert.Equal(MonkClass(family), temp.App.GetCharacter(id).Character.Classes[0].Class);
    }

    [Fact]
    public void After_the_class_update_the_v12_effects_apply()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd51;
        var shield = Armor(family, "Shield");
        var id = temp.App.SaveCharacter(Monk(family, 10, [new(shield, Equipped: true)])).Character.Id;

        var updated = temp.App.ApplyUpdate(id, MonkClass(family), NewClass51, confirm: true);
        var sheet = updated.Sheet;

        Assert.Equal(NewClass51, updated.Character.Classes[0].Class);
        AssertClean(sheet);
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
        Assert.Equal(30, sheet.Field(FieldIds.Speed).Value); // the shield now switches the bonus off
        Assert.Equal(["poisoned"], sheet.ConditionImmunities!.Select(c => c.Condition));
        Assert.Equal(AutomationStatus.Automatic, sheet.Features!.Single(f => f.Name == "Evasion").Automation);
        Assert.Contains(sheet.Active!, r => r == OpenHand(family));
    }

    // ---- C2-6: the 5.2.1 v12 revisions -----------------------------------------------------------------------------------------

    [Fact]
    public void The_5_2_1_unarmored_defense_and_movement_stop_with_armor_or_shield()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd521;
        var shield = new EquipmentEntry(Armor(family, "Shield"), Equipped: true);
        var leather = new EquipmentEntry(Armor(family, LeatherName(family)), Equipped: true);

        Assert.Equal(40, temp.App.SaveCharacter(V12Monk52(2)).Sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(30, temp.App.SaveCharacter(V12Monk52(2, [shield])).Sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(30, temp.App.SaveCharacter(V12Monk52(2, [leather])).Sheet.Field(FieldIds.Speed).Value);
        // The old revision adds the bonus regardless.
        Assert.Equal(40, temp.App.SaveCharacter(Monk(family, 2, [shield])).Sheet.Field(FieldIds.Speed).Value);

        // Dex 3, Wis 4: bare 10 + 3 + 4 = 17. The old revision keeps the replace with a shield; the new one switches it off, leaving the ordinary 10 + Dex (13).
        AbilityScores wise = new(10, 16, 14, 8, 18, 8);
        Assert.Equal(17, temp.App.SaveCharacter(V12Monk(1, null, wise, family)).Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(17, temp.App.SaveCharacter(Monk(family, 1, [shield], wise)).Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(13, temp.App.SaveCharacter(V12Monk(1, [shield], wise, family)).Sheet.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void The_5_2_1_slow_fall_reduces_by_five_per_level()
    {
        using var temp = new TempApp();
        var slowFall = V12(420108, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Monk52(4)).Character.Id;

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Slow Fall").Automation);
        var fall = temp.App.PreviewDamage(new(id, 30, null, [new(slowFall.ContentId, "slowFall")], ResponseSource.Fall));
        Assert.Empty(fall.Notes);
        Assert.Equal(20, fall.Reduction);
        Assert.Null(Assert.Single(V12Pack521.Revisions.Single(r => r.Name == "Slow Fall").Effects.OfType<ResponseEffect>()).ResourceId);
    }

    [Fact]
    public void The_5_2_1_evasion_is_off_while_incapacitated()
    {
        using var temp = new TempApp();
        var evasion = V12(420112, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Monk52(7)).Character.Id;

        var success = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        Assert.Empty(success.Notes);
        Assert.Equal(0, success.Outcome.Adjusted);
        var failure = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Equal(10, failure.Outcome.Adjusted);

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Evasion").Automation);
        Assert.All(V12Pack521.Revisions.Single(r => r.Name == "Evasion").Effects.OfType<ResponseEffect>(), e => Assert.Equal("incapacitated", e.UnlessCondition));
    }

    [Fact]
    public void The_5_2_1_deflect_attacks_reduces_physical_damage_only()
    {
        using var temp = new TempApp();
        var deflect = V12(420106, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Monk52(3)).Character.Id;

        // Dex +3 and level 3 on a rolled 6: 12 less.
        var preview = temp.App.PreviewDamage(new(id, 20, "slashing", [new(deflect.ContentId, "deflectAttacks", Rolled: 6)], ResponseSource.Attack));
        Assert.Empty(preview.Notes);
        Assert.Equal(8, preview.Outcome.Adjusted);

        Assert.DoesNotContain(temp.App.PreviewDamage(new(id, 20, "fire", null, ResponseSource.Attack)).Offers, o => o.Response.ResponseId == "deflectAttacks");

        var effects = V12Pack521.Revisions.Single(r => r.Name == "Deflect Attacks").Effects;
        var redirect = Assert.Single(effects.OfType<RollEffect>());
        Assert.Equal(("deflectAttacksRedirect", "focus", "1"), (redirect.RollId, redirect.ResourceId, redirect.Cost));
        Assert.Null(Assert.Single(effects.OfType<ResponseEffect>()).ResourceId);
        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Deflect Attacks").Automation);
    }

    [Fact]
    public void Deflect_energy_supersedes_deflect_attacks_from_level_13()
    {
        using var temp = new TempApp();
        var attacks = V12(420106, RulesFamilies.Srd521);
        var energy = V12(420116, RulesFamilies.Srd521);

        var supersedes = Assert.Single(V12Pack521.Revisions.Single(r => r.Name == "Deflect Energy").Effects.OfType<ResponseEffect>()).Supersedes;
        Assert.Equal((attacks.ContentId, "deflectAttacks"), (supersedes!.Content, supersedes.ResponseId));

        var twelve = temp.App.SaveCharacter(V12Monk52(12)).Character.Id;
        var before = temp.App.PreviewDamage(new(twelve, 20, "slashing", null, ResponseSource.Attack));
        Assert.Contains(before.Offers, o => o.Response.ResponseId == "deflectAttacks");
        Assert.DoesNotContain(before.Offers, o => o.Response.ResponseId == "deflectEnergy");

        var thirteen = temp.App.SaveCharacter(V12Monk52(13)).Character.Id;
        var after = temp.App.PreviewDamage(new(thirteen, 20, "slashing", null, ResponseSource.Attack));
        Assert.DoesNotContain(after.Offers, o => o.Response.ResponseId == "deflectAttacks");
        Assert.Contains(after.Offers, o => o.Response.ContentId == energy.ContentId && o.Response.ResponseId == "deflectEnergy");
        var fire = temp.App.PreviewDamage(new(thirteen, 20, "fire", null, ResponseSource.Attack));
        Assert.Contains(fire.Offers, o => o.Response.ResponseId == "deflectEnergy");
        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(thirteen).Sheet.Features!.Single(f => f.Name == "Deflect Energy").Automation);
    }

    [Fact]
    public void The_5_2_1_v12_Monk_keeps_every_unchanged_grant_and_the_Open_Hand()
    {
        var family = RulesFamilies.Srd521;
        var old = MonkPack(family).Revisions.Single(r => r.Kind == ContentKind.Class);
        var revised = V12Pack521.Revisions.Single(r => r.Kind == ContentKind.Class);
        var changed = new[] { 420102L, 420104, 420106, 420108, 420112, 420116, 420121 };
        IEnumerable<(string, ContentReference)> Grants(ContentRevision r) => r.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => (g.Id, g.Content!));
        var oldGrants = Grants(old).ToDictionary(g => g.Item1, g => g.Item2);
        foreach (var (id, content) in Grants(revised))
            Assert.Equal(changed.Any(n => V12(n, family).ContentId == content.ContentId) ? V12(int.Parse(content.ContentId.ToString()[^6..]), family) : oldGrants[id], content);
        Assert.Equal(oldGrants.Count, Grants(revised).Count());
        Assert.Equal(changed.Length, V12Pack521.Revisions.Count(r => r.Kind == ContentKind.Feature));
        Assert.Equal(changed.Length + 1, V12Pack521.Revisions.Count);
    }

    [Fact]
    public void A_C2_3_5_2_1_Monk_is_offered_the_v12_class_and_the_effects_apply_after_the_update()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd521;
        var shield = Armor(family, "Shield");
        var id = temp.App.SaveCharacter(Monk(family, 10, [new(shield, Equipped: true)])).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        AssertClean(sheet);
        Assert.Equal(50, sheet.Field(FieldIds.Speed).Value); // the old speed bonus ignores the shield
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Evasion").Automation);

        var offer = Assert.Single(temp.App.AvailableUpdates(id), o => o.Role == ReferenceRole.Class && o.Name == "Monk");
        Assert.Equal((MonkClass(family), NewClass(family), true), (offer.From, offer.To, offer.Bundled));
        Assert.Equal(MonkClass(family), temp.App.GetCharacter(id).Character.Classes[0].Class);

        var updated = temp.App.ApplyUpdate(id, MonkClass(family), NewClass(family), confirm: true);
        Assert.Equal(NewClass(family), updated.Character.Classes[0].Class);
        AssertClean(updated.Sheet);
        Assert.All(updated.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
        Assert.Equal(30, updated.Sheet.Field(FieldIds.Speed).Value); // the shield now switches the bonus off
        Assert.Equal(AutomationStatus.Automatic, updated.Sheet.Features!.Single(f => f.Name == "Evasion").Automation);
        Assert.Contains(updated.Sheet.Active!, r => r == OpenHand(family));
    }

    private static readonly string[] FixtureLevels =["1", "3", "5", "11", "17", "20"];

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
    public void Fixture_monks_calculate_with_a_trace(string family, string level)
    {
        using var temp = new TempApp();
        var fixture = TempApp.LoadFixture<Character>($"characters/{family}-monk-l{level}.json");

        var view = temp.App.SaveCharacter(fixture);

        Assert.Equal(MonkClass(view.Character.RulesFamily), view.Character.Classes[0].Class);
        Assert.Equal(int.Parse(level), view.Character.Classes[0].Level);
        AssertClean(view.Sheet);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.ArmorClass).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.HitPoints).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.Speed).Trace);
        Assert.NotEmpty(view.Sheet.Scales!.Single(s => s.ScaleId == "martialArts").Label);
        Assert.All(view.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
    }
}
