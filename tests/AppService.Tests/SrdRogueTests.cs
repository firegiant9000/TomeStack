using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// C2-2 on the bundled SRD content (srd-&lt;family&gt;-rogue.json): a Rogue of each family, levels 1-20, side by side.
/// Values are the SRD class tables' (docs/licensing/srd-pack-review.md, "C2-2").
/// </summary>
public class SrdRogueTests
{
    private static readonly string[] Families = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static string Prefix(string family) => family == RulesFamilies.Srd51 ? "51" : "52";

    private static string PackName(string family) => family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1";

    private static ContentReference Ref(string family, long content, long? revision = null) =>
        new(Guid.Parse($"{Prefix(family)}c00000-0000-4000-8000-{content:D12}"), Guid.Parse($"{Prefix(family)}e00000-0000-4000-8000-{revision ?? content:D12}"));

    private static ContentPack RoguePack(string family) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{PackName(family)}-rogue.json");

    private static ContentPack Pack(string family, string name) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{PackName(family)}-{name}.json");

    private static ContentReference RogueClass(string family) => Ref(family, 410001);

    private static ContentReference Thief(string family) => Ref(family, 410002);

    private static string SubclassChoice(string family) => family == RulesFamilies.Srd51 ? "roguish-archetype" : "rogue-subclass";

    /// <summary>A Rogue-pack revision by exact name (the Rogue pack only: "Expertise" and "Weapon Mastery" exist in other classes too).</summary>
    private static ContentReference Named(string family, string name) => RoguePack(family).Revisions.Single(r => r.Name == name).Reference;

    private static ContentReference Skill(string family, string name) => Named(family, $"Rogue Skill: {name}");

    private static ContentReference Expert(string family, string name) => Named(family, $"Rogue Expertise: {name}");

    private static readonly string[] DefaultSkills = ["Acrobatics", "Deception", "Perception", "Stealth"];

    private static readonly string[] DefaultExpertise = ["Perception", "Stealth"];

    private static readonly string[] SecondExpertise = ["Acrobatics", "Deception"];

    /// <summary>
    /// A Rogue of <paramref name="level"/> with the choices answered: Acrobatics, Deception, Perception and Stealth, Expertise in
    /// two (Perception and Stealth unless <paramref name="expertise"/> says otherwise), a second pair from level 6 and the Thief from
    /// level 3. Str 10, Dex 16 (+3), Con 14 (+2), Wis 12 (+1).
    /// </summary>
    private static Character Rogue(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, string[]? expertise = null)
    {
        var cls = RogueClass(family);
        List<ChoiceSelection> choices =
        [
            new(cls, "rogue-skills", [.. DefaultSkills.Select(s => Skill(family, s))]),
            new(cls, "rogue-expertise", [.. (expertise ?? DefaultExpertise).Select(s => Expert(family, s))]),
        ];
        if (level >= 3)
            choices.Add(new(cls, SubclassChoice(family), [Thief(family)]));
        if (level >= 6)
            choices.Add(new(cls, "rogue-expertise-6", [.. SecondExpertise.Select(s => Expert(family, s))]));
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD Rogue", RulesFamily = family, Level = level,
            Classes = [new(cls, level)],
            BaseAbilities = scores ?? new(10, 16, 14, 10, 12, 8),
            Choices = choices,
            Equipment = equipment ?? [],
        };
    }

    private static readonly string[] Broken = ["choice.unresolved", "content.missing", "effect.invalid-formula", "choice.invalid-option"];

    private static void AssertClean(CharacterSheet sheet) => Assert.DoesNotContain(sheet.Diagnostics, d => Broken.Contains(d.Code));

    private static ContentReference Weapon(string family, string name) =>
        Pack(family, "equipment").Revisions.Last(r => r.Name == name).Reference;

    private static string HandCrossbow(string family) => family == RulesFamilies.Srd51 ? "Crossbow, hand" : "Hand Crossbow";

    [Theory]
    // level, Sneak Attack dice (the same in both families)
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 4)]
    [InlineData(9, 5)]
    [InlineData(10, 5)]
    [InlineData(11, 6)]
    [InlineData(12, 6)]
    [InlineData(13, 7)]
    [InlineData(14, 7)]
    [InlineData(15, 8)]
    [InlineData(16, 8)]
    [InlineData(17, 9)]
    [InlineData(18, 9)]
    [InlineData(19, 10)]
    [InlineData(20, 10)]
    public void Calculates_levels_1_to_20_side_by_side(int level, int sneakDice)
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Rogue(family, level)).Sheet;

            Assert.Equal(sneakDice, sheet.Scales!.Single(s => s.ScaleId == "sneakAttack").Value);
            // d8: 8 + Con 2 at level 1, then 5 + 2 per level (SRD fixed value).
            Assert.Equal(10 + (7 * (level - 1)), sheet.Field(FieldIds.HitPoints).Value);
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Save(Ability.Dex)).Mark);
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Save(Ability.Int)).Mark);
            AssertClean(sheet);
            Assert.NotEmpty(sheet.Field(FieldIds.Skill("stealth")).Trace);
            Assert.NotEmpty(sheet.Field(FieldIds.ArmorClass).Trace);
            Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{family} {c.ChoiceId}"));
        }
    }

    [Fact]
    public void Expertise_doubles_the_proficiency_bonus_on_the_picked_skills()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Rogue(family, 1)).Sheet;

            var stealth = sheet.Field(FieldIds.Skill("stealth"));
            Assert.Equal(3 + (2 * 2), stealth.Value); // Dex +3, twice the proficiency bonus (+2)
            Assert.Equal(ProficiencyMark.Expertise, stealth.Mark);
            Assert.Contains(stealth.Trace, t => t.Description.StartsWith("Expertise", StringComparison.Ordinal) && t.Amount == 4);
            var perception = sheet.Field(FieldIds.Skill("perception"));
            Assert.Equal(1 + 4, perception.Value); // Wis +1
            Assert.Equal(ProficiencyMark.Expertise, perception.Mark);

            var acrobatics = sheet.Field(FieldIds.Skill("acrobatics")); // proficient, not picked for Expertise
            Assert.Equal((3 + 2, ProficiencyMark.Proficient), (acrobatics.Value, acrobatics.Mark));
            var athletics = sheet.Field(FieldIds.Skill("athletics")); // not proficient
            Assert.Equal((0, ProficiencyMark.None), (athletics.Value, athletics.Mark));
            Assert.DoesNotContain(athletics.Trace, t => t.Description.StartsWith("Expertise", StringComparison.Ordinal) || t.Description.StartsWith("Proficiency bonus from", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Expertise_on_an_unproficient_skill_still_doubles()
    {
        // Documented limitation: CollectProficiencies (Calculation.cs) does not check that the character is proficient in the
        // skill, so an Expertise pick on a skill the Rogue did not take still adds twice the proficiency bonus. The pick's own
        // summary says TomeStack does not check it.
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Rogue(family, 1, expertise: ["Athletics", "Stealth"])).Sheet;

            var athletics = sheet.Field(FieldIds.Skill("athletics"));
            Assert.Equal((0 + 4, ProficiencyMark.Expertise), (athletics.Value, athletics.Mark));
            AssertClean(sheet);
        }
    }

    [Fact]
    public void The_second_expertise_choice_opens_at_level_6()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var five = temp.App.SaveCharacter(Rogue(family, 5)).Sheet;
            Assert.DoesNotContain(five.Choices!, c => c.ChoiceId == "rogue-expertise-6");
            Assert.Equal(ProficiencyMark.Proficient, five.Field(FieldIds.Skill("acrobatics")).Mark);

            var six = temp.App.SaveCharacter(Rogue(family, 6)).Sheet;
            var second = six.Choices!.Single(c => c.ChoiceId == "rogue-expertise-6");
            Assert.True(second.Resolved);
            Assert.Equal(2, second.Count);
            var acrobatics = six.Field(FieldIds.Skill("acrobatics"));
            Assert.Equal((3 + (2 * 3), ProficiencyMark.Expertise), (acrobatics.Value, acrobatics.Mark)); // Dex +3, proficiency bonus +3
            Assert.Equal(ProficiencyMark.Expertise, six.Field(FieldIds.Skill("deception")).Mark);
            AssertClean(six);

            // Unanswered, the choice is flagged rather than dropped.
            var open = temp.App.SaveCharacter(Rogue(family, 6) with { Choices = [.. Rogue(family, 6).Choices.Where(c => c.ChoiceId != "rogue-expertise-6")] }).Sheet;
            Assert.False(open.Choices!.Single(c => c.ChoiceId == "rogue-expertise-6").Resolved);
        }
    }

    [Fact]
    public void Weapon_proficiencies_follow_each_family()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var rapier = Weapon(family, "Rapier");
            var longsword = Weapon(family, "Longsword");
            var crossbow = Weapon(family, HandCrossbow(family));
            var sheet = temp.App.SaveCharacter(Rogue(family, 1, [new(rapier, Equipped: true), new(longsword, Equipped: true), new(crossbow, Equipped: true)])).Sheet;

            bool Proficient(ContentReference item) => sheet.Attacks!.Single(a => a.Item == item).Proficient;
            Assert.True(Proficient(rapier), $"{family} rapier");
            Assert.True(Proficient(crossbow), $"{family} hand crossbow");
            // 5.1 names the longsword; 5.2.1 allows only Finesse or Light martial weapons.
            Assert.Equal(family == RulesFamilies.Srd51, Proficient(longsword));
        }
    }

    [Fact]
    public void A_multiclass_rogue_gets_one_skill_and_light_armor()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var fighterPack = Pack(family, "fighter");
            var fighter = fighterPack.Revisions.Last(r => r.Name == "Fighter" && r.Kind == ContentKind.Class).Reference;
            var rogue = RogueClass(family);
            var character = new Character
            {
                Id = Guid.NewGuid(), Name = "Test Fighter Rogue", RulesFamily = family, Level = 2,
                Classes = [new(fighter, 1), new(rogue, 1)],
                BaseAbilities = new(14, 14, 12, 10, 10, 8),
                Choices = [new(rogue, "rogue-multiclass-skill", [Skill(family, "Stealth")])],
            };

            var sheet = temp.App.SaveCharacter(character).Sheet;

            var skill = sheet.Choices!.Single(c => c.SourceName == "Rogue" && c.ChoiceId == "rogue-multiclass-skill");
            Assert.Equal((1, true), (skill.Count, skill.Resolved));
            Assert.DoesNotContain(sheet.Choices!, c => c.ChoiceId == "rogue-skills");
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Skill("stealth")).Mark);
            // The saving throws come only with the starting class (Fighter: Str and Con).
            Assert.Equal(ProficiencyMark.None, sheet.Field(FieldIds.Save(Ability.Int)).Mark);
            Assert.Equal(ProficiencyMark.None, sheet.Field(FieldIds.Save(Ability.Dex)).Mark);
            Assert.Equal(ProficiencyMark.Proficient, sheet.Field(FieldIds.Save(Ability.Str)).Mark);

            // Light armor comes with every Rogue level: the Rogue's grant is not limited to the starting class.
            var grant = Assert.Single(RoguePack(family).Revisions.Single(r => r.Reference == rogue).Effects.OfType<GrantEffect>(),g => g.Target == "armor.light");
            Assert.Null(grant.OnlyAs);
        }
    }

    [Fact]
    public void A_wizard_who_took_a_level_of_Rogue_is_trained_for_light_armor()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var wizard = Pack(family, "classes").Revisions.Last(r => r.Name == "Wizard" && r.Kind == ContentKind.Class).Reference;
            var leather = Pack(family, "armor").Revisions.Single(r => r.Name is "Leather" or "Leather Armor").Reference;
            Character Build(params ClassLevel[] classes) => new()
            {
                Id = Guid.NewGuid(), Name = "Test Wizard Rogue", RulesFamily = family, Level = classes.Sum(c => c.Level),
                Classes = classes, BaseAbilities = new(10, 14, 12, 14, 10, 8), Equipment = [new(leather, Equipped: true)],
            };

            var wizardOnly = temp.App.SaveCharacter(Build(new ClassLevel(wizard, 1))).Sheet.Field(FieldIds.ArmorClass);
            Assert.Contains(wizardOnly.Warnings, w => w.Code == "equipment.armor-untrained");

            var both = temp.App.SaveCharacter(Build(new ClassLevel(wizard, 1), new ClassLevel(RogueClass(family), 1))).Sheet.Field(FieldIds.ArmorClass);
            Assert.DoesNotContain(both.Warnings, w => w.Code == "equipment.armor-untrained");
        }
    }

    [Fact]
    public void Slippery_Mind_adds_saves_at_15()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var fourteen = temp.App.SaveCharacter(Rogue(family, 14)).Sheet;
            Assert.Equal(ProficiencyMark.None, fourteen.Field(FieldIds.Save(Ability.Wis)).Mark);
            Assert.Equal(ProficiencyMark.None, fourteen.Field(FieldIds.Save(Ability.Cha)).Mark);

            var fifteen = temp.App.SaveCharacter(Rogue(family, 15)).Sheet;
            Assert.Equal(ProficiencyMark.Proficient, fifteen.Field(FieldIds.Save(Ability.Wis)).Mark);
            // 5.1 Slippery Mind: Wisdom only; 5.2.1: Wisdom and Charisma.
            Assert.Equal(family == RulesFamilies.Srd51 ? ProficiencyMark.None : ProficiencyMark.Proficient, fifteen.Field(FieldIds.Save(Ability.Cha)).Mark);
        }
    }

    [Fact]
    public void Stroke_of_Luck_is_a_pool_of_one_that_a_short_rest_restores()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var nineteen = temp.App.SaveCharacter(Rogue(family, 19)).Sheet;
            Assert.DoesNotContain(nineteen.Resources!, r => r.ResourceId == "strokeOfLuck");

            var id = temp.App.SaveCharacter(Rogue(family, 20)).Character.Id;
            var pool = temp.App.GetCharacter(id).Sheet.Resources!.Single(r => r.ResourceId == "strokeOfLuck");
            Assert.Equal((1, 1), (pool.Maximum, pool.Current));
            Assert.Contains(pool.Recoveries, r => r.On == RestPeriod.ShortRest);

            var stamp = temp.App.GetCharacter(id).Character.UpdatedAt;
            var used = temp.App.Play(new(id, PlayActionKind.UseAction, Confirm: true, ContentId: Ref(family, 410117).ContentId, RollId: "strokeOfLuck", ExpectedUpdatedAt: stamp)).Sheet;
            Assert.Equal(0, used.Resources!.Single(r => r.ResourceId == "strokeOfLuck").Current);

            var preview = temp.App.PreviewRest(id, RestPeriod.ShortRest);
            var rested = temp.App.Rest(new(id, RestPeriod.ShortRest, Confirm: true, Basis: preview.Basis)).Sheet;
            Assert.Equal(1, rested.Resources!.Single(r => r.ResourceId == "strokeOfLuck").Current);
        }
    }

    [Fact]
    public void Supreme_Sneak_is_assisted_stealth_advantage_in_5_1()
    {
        using var temp = new TempApp();
        var eight = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd51, 8)).Sheet.Field(FieldIds.Skill("stealth"));
        Assert.DoesNotContain(eight.Mode?.Reasons ?? [], r => r.Source == "Supreme Sneak");

        var classic = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd51, 9)).Sheet.Field(FieldIds.Skill("stealth"));
        var reason = Assert.Single(classic.Mode!.Reasons, r => r.Source == "Supreme Sneak");
        Assert.Equal((RollMode.Advantage, AutomationStatus.Assisted, false), (reason.Mode, reason.Automation, reason.IsAutomatic));
        Assert.Equal(RollMode.Normal, classic.Mode.Automatic); // offered, not applied: the sheet cannot see how far the Rogue moved

        // 5.2.1's Supreme Sneak is the Cunning Strike option (a reference feature); nothing touches the Stealth roll.
        var modern = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd521, 9)).Sheet.Field(FieldIds.Skill("stealth"));
        Assert.Equal(RollMode.Normal, modern.Mode?.Automatic ?? RollMode.Normal);
        Assert.Empty(modern.Mode?.Reasons ?? []);
    }

    [Fact]
    public void Weapon_mastery_shows_for_a_5_2_1_rogue_only()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sword = Weapon(family, "Shortsword");
            var sheet = temp.App.SaveCharacter(Rogue(family, 1, [new(sword, Equipped: true)])).Sheet;

            var mastery = sheet.Attacks!.Single(a => a.Item == sword).Mastery;
            if (family == RulesFamilies.Srd521)
            {
                Assert.Equal("Vex", mastery);
                Assert.Contains(sheet.Features!, f => f.Name == "Weapon Mastery");
            }
            else
            {
                Assert.Null(mastery); // SRD 5.1 weapons carry no mastery
                Assert.DoesNotContain(sheet.Features!, f => f.Name == "Weapon Mastery");
            }
        }
    }

    [Fact]
    public void Reference_features_are_listed_as_reference()
    {
        using var temp = new TempApp();
        string[] both = ["Sneak Attack", "Thieves’ Cant", "Cunning Action", "Uncanny Dodge", "Evasion", "Reliable Talent", "Elusive", "Second-Story Work"];
        var names = new Dictionary<string, string[]>
        {
            [RulesFamilies.Srd521] = [.. both, "Steady Aim", "Cunning Strike", "Weapon Mastery"],
            [RulesFamilies.Srd51] = [.. both, "Blindsense"],
        };
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Rogue(family, 20)).Sheet;
            // Scoped to this family's Rogue revisions: "Expertise" and "Weapon Mastery" exist in other classes too.
            var rogueRevisions = RoguePack(family).Revisions.Select(r => r.RevisionId).ToHashSet();
            var features = sheet.Features!.Where(f => rogueRevisions.Contains(f.Content.RevisionId)).ToList();
            foreach (var name in names[family])
            {
                var feature = Assert.Single(features, f => f.Name == name);
                Assert.True(feature.Automation == AutomationStatus.Reference, $"{family}: {name} is {feature.Automation}");
            }
        }
    }

    [Fact]
    public void The_rogue_packs_add_nothing_to_existing_characters()
    {
        string[] rogueFiles = ["TomeStack.Content.srd-5.1-rogue.json", "TomeStack.Content.srd-5.2.1-rogue.json"];
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

        var without = Catalog(TomeStackApp.BundledPacks.Except(rogueFiles));
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

        // Nothing outside the Rogue packs mentions a Rogue id, and no other revision extends a Rogue choice.
        var rogueRevisions = rogueFiles.SelectMany(f => TomeStackApp.LoadBundledPack(f).Revisions).ToList();
        var rogueGuids = rogueRevisions.SelectMany(r => new[] { r.ContentId, r.RevisionId }).Distinct().Select(g => g.ToString("D")).ToList();
        foreach (var file in TomeStackApp.BundledPacks.Except(rogueFiles))
        {
            var pack = TomeStackApp.LoadBundledPack(file);
            var json = TempApp.Json(pack.Revisions);
            Assert.DoesNotContain(rogueGuids, g => json.Contains(g, StringComparison.OrdinalIgnoreCase));
            var rogueContent = rogueRevisions.Select(r => r.ContentId).ToHashSet();
            Assert.DoesNotContain(pack.Revisions, r => r.ExtendsChoice is { } e && rogueContent.Contains(e.ContentId));
        }
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
    public void Fixture_rogues_calculate_with_a_trace(string family, string level)
    {
        using var temp = new TempApp();
        var fixture = TempApp.LoadFixture<Character>($"characters/{family}-rogue-l{level}.json");

        var view = temp.App.SaveCharacter(fixture);

        Assert.Equal(RogueClass(view.Character.RulesFamily), view.Character.Classes[0].Class);
        Assert.Equal(int.Parse(level), view.Character.Classes[0].Level);
        AssertClean(view.Sheet);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.ArmorClass).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.HitPoints).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.Skill("stealth")).Trace);
        Assert.NotEmpty(view.Sheet.Scales!.Single(s => s.ScaleId == "sneakAttack").Label);
        Assert.All(view.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
    }
}
