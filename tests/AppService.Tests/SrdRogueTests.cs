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

    /// <summary>The 5.1 Rogue as C2-6 revised it (srd-5.1-rogue-v12.json): the new class revision and the v12 feature revisions it grants.</summary>
    private static ContentReference NewClass51 => Ref(RulesFamilies.Srd51, 410001, 12410001);

    private static ContentReference V12(long content, string family = RulesFamilies.Srd51) => Ref(family, content, 12000000 + (content % 1000000));

    private static ContentPack V12Pack51 => TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-rogue-v12.json");

    /// <summary>The 5.2.1 Rogue and Thief as C2-6 revised them (srd-5.2.1-rogue-v12.json).</summary>
    private static ContentReference NewClass52 => Ref(RulesFamilies.Srd521, 410001, 12410001);

    private static ContentReference NewThief52 => V12(410002, RulesFamilies.Srd521);

    private static ContentPack V12Pack521 => TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-rogue-v12.json");

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
    private static Character Rogue(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, string[]? expertise = null, ContentReference? cls = null, ContentReference? thief = null)
    {
        cls ??= RogueClass(family);
        List<ChoiceSelection> choices =
        [
            new(cls, "rogue-skills", [.. DefaultSkills.Select(s => Skill(family, s))]),
            new(cls, "rogue-expertise", [.. (expertise ?? DefaultExpertise).Select(s => Expert(family, s))]),
        ];
        if (level >= 3)
            choices.Add(new(cls, SubclassChoice(family), [thief ?? Thief(family)]));
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
            var grant = Assert.Single(RoguePack(family).Revisions.Single(r => r.Reference == rogue).Effects.OfType<GrantEffect>(), g => g.Target == "armor.light");
            Assert.Null(grant.OnlyAs);
        }
    }

    [Fact]
    public void A_wizard_who_took_a_level_of_Rogue_gets_light_armor_but_no_rogue_weapons()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var wizard = Pack(family, "classes").Revisions.Last(r => r.Name == "Wizard" && r.Kind == ContentKind.Class).Reference;
            var leather = Pack(family, "armor").Revisions.Single(r => r.Name is "Leather" or "Leather Armor").Reference;
            var rapier = Weapon(family, "Rapier");
            Character Build(params ClassLevel[] classes) => new()
            {
                Id = Guid.NewGuid(), Name = "Test Wizard Rogue", RulesFamily = family, Level = classes.Sum(c => c.Level),
                Classes = classes, BaseAbilities = new(10, 14, 12, 14, 10, 8), Equipment = [new(leather, Equipped: true), new(rapier, Equipped: true)],
            };

            var wizardSheet = temp.App.SaveCharacter(Build(new ClassLevel(wizard, 1))).Sheet;
            Assert.Contains(wizardSheet.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained");
            Assert.False(wizardSheet.Attacks!.Single(a => a.Item == rapier).Proficient, $"{family} wizard rapier");

            var bothSheet = temp.App.SaveCharacter(Build(new ClassLevel(wizard, 1), new ClassLevel(RogueClass(family), 1))).Sheet;
            Assert.DoesNotContain(bothSheet.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained");
            // The Rogue's weapon training comes only with the starting class (onlyAs), so the Wizard 1 / Rogue 1 has none of it.
            Assert.False(bothSheet.Attacks!.Single(a => a.Item == rapier).Proficient, $"{family} wizard/rogue rapier");
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
        var names = new Dictionary<string, string[]>
        {
            // C2-6: on the v12 class and Thief the 5.2.1 Sneak Attack is Assisted, Steady Aim, Uncanny Dodge, Evasion, Reliable Talent,
            // Elusive and Second-Story Work are Automatic, and no effect removes dice, so the Cunning Strikes stay Reference.
            [RulesFamilies.Srd521] = ["Thieves’ Cant", "Cunning Action", "Cunning Strike", "Improved Cunning Strike", "Devious Strikes", "Weapon Mastery",
                "Expertise", "Ability Score Improvement", "Fast Hands", "Use Magic Device", "Thief’s Reflexes", "Epic Boon", "Supreme Sneak"],
            // C2-6: on the v12 class the 5.1 Sneak Attack is Assisted and Uncanny Dodge, Evasion, Reliable Talent and Elusive are Automatic.
            [RulesFamilies.Srd51] = ["Thieves’ Cant", "Cunning Action", "Second-Story Work", "Blindsense",
                "Expertise", "Ability Score Improvement", "Fast Hands", "Use Magic Device", "Thief’s Reflexes"],
        };
        foreach (var family in Families)
        {
            var v12 = family == RulesFamilies.Srd51;
            var sheet = temp.App.SaveCharacter(Rogue(family, 20, cls: v12 ? NewClass51 : NewClass52, thief: v12 ? null : NewThief52)).Sheet;
            // Scoped to this family's Rogue revisions: "Expertise" and "Weapon Mastery" exist in other classes too.
            var rogueRevisions = RoguePack(family).Revisions.Select(r => r.RevisionId).ToHashSet();
            var features = sheet.Features!.Where(f => rogueRevisions.Contains(f.Content.RevisionId)).ToList();
            foreach (var name in names[family])
            {
                var feature = Assert.Single(features, f => f.Name == name);
                Assert.True(feature.Automation == AutomationStatus.Reference, $"{family}: {name} is {feature.Automation}");
            }
            // The exact set (see docs/features/effect-vocabulary-coverage.md): nothing else in the Rogue is Reference, so an Automatic
            // feature cannot silently become one.
            var actual = features.Where(f => f.Automation == AutomationStatus.Reference).Select(f => f.Name).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(names[family].Order(StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void The_rogue_packs_add_nothing_to_existing_characters()
    {
        string[] rogueFiles = ["TomeStack.Content.srd-5.1-rogue.json", "TomeStack.Content.srd-5.2.1-rogue.json", "TomeStack.Content.srd-5.1-rogue-v12.json", "TomeStack.Content.srd-5.2.1-rogue-v12.json"];
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
            // A creation guide (ADR-024) only advises the builder and changes no calculation, so it may name the Rogue (guideFor).
            var json = TempApp.Json(pack.Revisions.Where(r => r.Kind != ContentKind.CreationGuide).ToList());
            // C2-6: the v12 Monk's Evasion names the v12 Rogue's (`supersedes`), so a Rogue/Monk halves once. That is the one allowed mention.
            var allowed = file.EndsWith("-monk-v12.json", StringComparison.Ordinal)
                ? new[] { Ref(file.Contains("5.1", StringComparison.Ordinal) ? RulesFamilies.Srd51 : RulesFamilies.Srd521, 410110).ContentId.ToString("D") }
                : [];
            Assert.DoesNotContain(rogueGuids.Except(allowed), g => json.Contains(g, StringComparison.OrdinalIgnoreCase));
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

    // ---- C2-6: the 5.1 v12 revisions ---------------------------------------------------------------------------------------

    private static Character V12Rogue(int level, IReadOnlyList<EquipmentEntry>? equipment = null) =>
        Rogue(RulesFamilies.Srd51, level, equipment, cls: NewClass51);

    private static AttackEntry AttackOf(CharacterSheet sheet, ContentReference item) => sheet.Attacks!.Single(a => a.Item == item);

    [Theory]
    [InlineData(1, "1d6")]
    [InlineData(5, "3d6")]
    [InlineData(11, "6d6")]
    [InlineData(20, "10d6")]
    public void Sneak_attack_offers_scaled_d6_once_per_turn(int level, string dice)
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd51, "Shortsword");
        var sheet = temp.App.SaveCharacter(V12Rogue(level, [new(sword, Equipped: true)])).Sheet;

        var rider = Assert.Single(AttackOf(sheet, sword).Riders!, r => r.OncePerTurn);
        Assert.Equal(dice, rider.Dice);
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Name == "Sneak Attack").Automation);
        AssertClean(sheet);
    }

    private static CharacterSheet Sheet(TempApp temp, int level) => temp.App.SaveCharacter(V12Rogue(level)).Sheet;

    [Fact]
    public void Uncanny_dodge_halves_an_attack()
    {
        using var temp = new TempApp();
        var uncanny = V12(410109);
        var id = temp.App.SaveCharacter(V12Rogue(5)).Character.Id;

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Uncanny Dodge").Automation);
        var preview = temp.App.PreviewDamage(new(id, 20, "slashing", [new(uncanny.ContentId, "uncannyDodge")], ResponseSource.Attack));
        Assert.Empty(preview.Notes);
        Assert.Equal(10, preview.Reduction);
        Assert.Equal(10, preview.Outcome.Adjusted);

        // Not offered for a fall.
        var fall = temp.App.PreviewDamage(new(id, 20, null, [new(uncanny.ContentId, "uncannyDodge")], ResponseSource.Fall));
        Assert.Equal(20, fall.Outcome.Adjusted);

        // Taking it is free: the reaction is spent by hand, so no resource is charged.
        var stamp = temp.App.GetCharacter(id).Character.UpdatedAt;
        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 20, DamageType: "slashing", ExpectedUpdatedAt: stamp, Responses: [new(uncanny.ContentId, "uncannyDodge")], DamageFrom: ResponseSource.Attack));
        Assert.Equal(10, view.Damage!.Adjusted);
        Assert.DoesNotContain(view.Sheet.Resources ?? [], r => r.ResourceId == "uncannyDodgeReaction" || r.Label == "Reaction");
    }

    [Fact]
    public void Uncanny_dodge_has_no_reaction_pool_on_a_level_5_rogue()
    {
        using var temp = new TempApp();
        var sheet = Sheet(temp, 5);

        Assert.DoesNotContain(sheet.Resources ?? [], r => r.ResourceId == "uncannyDodgeReaction" || r.Label == "Reaction");
    }

    [Fact]
    public void Evasion_success_takes_none_failure_takes_half()
    {
        using var temp = new TempApp();
        var evasion = V12(410110);
        var id = temp.App.SaveCharacter(V12Rogue(7)).Character.Id;

        var success = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        Assert.Empty(success.Notes);
        Assert.Equal(0, success.Outcome.Adjusted);
        Assert.DoesNotContain(success.Offers, o => o.Response.ResponseId == "evasionFailure");

        var failure = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Empty(failure.Notes);
        Assert.Equal(10, failure.Outcome.Adjusted);
        Assert.DoesNotContain(failure.Offers, o => o.Response.ResponseId == "evasionSuccess");

        // 5.1 Evasion has no "unless incapacitated" clause.
        Assert.All(TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-rogue-v12.json").Revisions.Single(r => r.Name == "Evasion").Effects.OfType<ResponseEffect>(), e => Assert.Null(e.UnlessCondition));
    }

    [Fact]
    public void Reliable_talent_floors_a_proficient_check_at_10()
    {
        using var temp = new TempApp();
        var ten = temp.App.SaveCharacter(V12Rogue(10)).Character.Id;
        Assert.Null(temp.App.Roll(new RollCommand(ten, Field: FieldIds.Skill("stealth"))).Floor);

        var id = temp.App.SaveCharacter(V12Rogue(11)).Character.Id;
        var stealth = temp.App.Roll(new RollCommand(id, Field: FieldIds.Skill("stealth")));
        Assert.Equal(10, stealth.Floor!.Face);
        Assert.Equal(["Reliable Talent"], stealth.Floor.Reasons.Select(r => r.Source));

        // Athletics is not a proficiency of this Rogue.
        Assert.Null(temp.App.Roll(new RollCommand(id, Field: FieldIds.Skill("athletics"))).Floor);
    }

    [Fact]
    public void Elusive_prevents_advantage_against()
    {
        using var temp = new TempApp();
        Assert.Null(Sheet(temp, 17).AttacksAgainst);

        var reason = Assert.Single(Sheet(temp, 18).AttacksAgainst!.Reasons, r => r.Source == "Elusive");
        Assert.Equal((RollMode.Advantage, true), (reason.Mode, reason.Prevent));

        // Not while incapacitated.
        var incapacitated = V12Rogue(18);
        var sheet = temp.App.SaveCharacter(incapacitated with { Play = incapacitated.Play with { Conditions = ["incapacitated"] } }).Sheet;
        Assert.DoesNotContain(sheet.AttacksAgainst?.Reasons ?? [], r => r.Source == "Elusive" && r.Prevent);
    }

    [Fact]
    public void The_v12_Rogue_keeps_every_unchanged_grant_and_the_Thief()
    {
        var old = RoguePack(RulesFamilies.Srd51).Revisions.Single(r => r.Kind == ContentKind.Class);
        var revised = V12Pack51.Revisions.Single(r => r.Kind == ContentKind.Class);
        var changed = new[] { 410102L, 410109, 410110, 410111, 410115 }.Select(n => V12(n).ContentId).ToHashSet();
        IEnumerable<(string, ContentReference)> Grants(ContentRevision r) => r.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => (g.Id, g.Content!));
        var oldGrants = Grants(old).ToDictionary(g => g.Item1, g => g.Item2);
        foreach (var (id, content) in Grants(revised))
            Assert.Equal(changed.Contains(content.ContentId) ? V12(int.Parse(content.ContentId.ToString()[^6..])) : oldGrants[id], content);
        Assert.Equal(oldGrants.Count, Grants(revised).Count());
    }

    // ---- C2-6: existing characters on the older 5.1 Rogue ---------------------------------------------------------------------

    private static Character PreV12Rogue(int level, ContentReference sword) => Rogue(RulesFamilies.Srd51, level, [new(sword, Equipped: true)]);

    [Fact]
    public void A_C2_2_Rogue_calculates_as_before_and_is_offered_the_v12_class()
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd51, "Shortsword");
        var id = temp.App.SaveCharacter(PreV12Rogue(18, sword)).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        AssertClean(sheet);
        Assert.DoesNotContain(AttackOf(sheet, sword).Riders ?? [], r => r.OncePerTurn);
        Assert.Null(sheet.AttacksAgainst);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Evasion").Automation);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Sneak Attack").Automation);

        var offer = Assert.Single(temp.App.AvailableUpdates(id), o => o.Role == ReferenceRole.Class && o.Name == "Rogue");
        Assert.Equal((RogueClass(RulesFamilies.Srd51), NewClass51, true), (offer.From, offer.To, offer.Bundled));
        Assert.Equal(RogueClass(RulesFamilies.Srd51), temp.App.GetCharacter(id).Character.Classes[0].Class);
    }

    [Fact]
    public void After_the_class_update_the_v12_effects_apply()
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd51, "Shortsword");
        var id = temp.App.SaveCharacter(PreV12Rogue(18, sword)).Character.Id;

        var updated = temp.App.ApplyUpdate(id, RogueClass(RulesFamilies.Srd51), NewClass51, confirm: true);
        var sheet = updated.Sheet;

        Assert.Equal(NewClass51, updated.Character.Classes[0].Class);
        AssertClean(sheet);
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
        Assert.Equal("9d6", AttackOf(sheet, sword).Riders!.Single(r => r.OncePerTurn).Dice);
        Assert.Contains(sheet.AttacksAgainst!.Reasons, r => r.Source == "Elusive" && r.Prevent);
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Name == "Sneak Attack").Automation);
        Assert.Contains(sheet.Active!, r => r == Thief(RulesFamilies.Srd51));
    }

    [Fact]
    public void The_5_1_Rogue_class_update_is_offered_to_a_character_on_every_older_revision()
    {
        using var temp = new TempApp();
        var id = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd51, 9)).Character.Id;
        var offers = temp.App.AvailableUpdates(id);

        Assert.Contains(offers, o => o.From == RogueClass(RulesFamilies.Srd51) && o.To == NewClass51);
        // The Thief is untouched by C2-6 in 5.1.
        Assert.DoesNotContain(offers, o => o.From == Thief(RulesFamilies.Srd51));
    }

    // ---- C2-6: the 5.2.1 v12 revisions -------------------------------------------------------------------------------------------

    private static Character V12Rogue52(int level, IReadOnlyList<EquipmentEntry>? equipment = null) =>
        Rogue(RulesFamilies.Srd521, level, equipment, cls: NewClass52, thief: NewThief52);

    private static CharacterSheet Sheet52(TempApp temp, int level) => temp.App.SaveCharacter(V12Rogue52(level)).Sheet;

    [Theory]
    [InlineData(1, "1d6")]
    [InlineData(5, "3d6")]
    [InlineData(11, "6d6")]
    [InlineData(20, "10d6")]
    public void Sneak_attack_52_offers_scaled_d6_once_per_turn(int level, string dice)
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd521, "Shortsword");
        var sheet = temp.App.SaveCharacter(V12Rogue52(level, [new(sword, Equipped: true)])).Sheet;

        var rider = Assert.Single(AttackOf(sheet, sword).Riders!, r => r.OncePerTurn);
        Assert.Equal(dice, rider.Dice);
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Name == "Sneak Attack").Automation);
        AssertClean(sheet);
    }

    [Fact]
    public void Steady_aim_gives_advantage_and_speed_0()
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd521, "Shortsword");
        var steady = V12(410106, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Rogue52(3, [new(sword, Equipped: true)])).Character.Id;

        var off = temp.App.GetCharacter(id).Sheet;
        Assert.Contains(off.Toggles!, t => t.ToggleId == "steadyAim" && !t.On);
        Assert.Null(AttackOf(off, sword).Mode);
        Assert.Equal(30, off.Field(FieldIds.Speed).Value);
        Assert.Equal(AutomationStatus.Automatic, off.Features!.Single(f => f.Name == "Steady Aim").Automation);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: steady.ContentId, ToggleId: "steadyAim")).Sheet;
        Assert.Equal(RollMode.Advantage, AttackOf(on, sword).Mode!.Automatic);
        Assert.Equal(0, on.Field(FieldIds.Speed).Value);

        var again = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: steady.ContentId, ToggleId: "steadyAim")).Sheet;
        Assert.Null(AttackOf(again, sword).Mode);
        Assert.Equal(30, again.Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void Second_story_work_gives_climb_speed_equal_to_speed()
    {
        using var temp = new TempApp();
        Assert.Equal(0, Sheet52(temp, 2).Field(FieldIds.ClimbSpeed).Value);

        var sheet = Sheet52(temp, 3);
        Assert.Equal(sheet.Field(FieldIds.Speed).Value, sheet.Field(FieldIds.ClimbSpeed).Value);
        Assert.Equal(30, sheet.Field(FieldIds.ClimbSpeed).Value);
        Assert.Equal(AutomationStatus.Automatic, sheet.Features!.Single(f => f.Name == "Second-Story Work").Automation);

        // The old Thief revision gives no climb speed.
        var old = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd521, 3, cls: NewClass52)).Sheet;
        Assert.Equal(0, old.Field(FieldIds.ClimbSpeed).Value);
        AssertClean(sheet);
    }

    [Fact]
    public void Uncanny_dodge_52_halves_an_attack_and_costs_nothing()
    {
        using var temp = new TempApp();
        var uncanny = V12(410109, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Rogue52(5)).Character.Id;

        Assert.Equal(AutomationStatus.Automatic, temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Name == "Uncanny Dodge").Automation);
        var preview = temp.App.PreviewDamage(new(id, 20, "slashing", [new(uncanny.ContentId, "uncannyDodge")], ResponseSource.Attack));
        Assert.Empty(preview.Notes);
        Assert.Equal(10, preview.Outcome.Adjusted);
        Assert.DoesNotContain(Sheet52(temp, 5).Resources ?? [], r => r.ResourceId == "uncannyDodgeReaction" || r.Label == "Reaction");

        var fall = temp.App.PreviewDamage(new(id, 20, null, [new(uncanny.ContentId, "uncannyDodge")], ResponseSource.Fall));
        Assert.Equal(20, fall.Outcome.Adjusted);
    }

    [Fact]
    public void Evasion_52_success_takes_none_failure_takes_half()
    {
        using var temp = new TempApp();
        var evasion = V12(410110, RulesFamilies.Srd521);
        var id = temp.App.SaveCharacter(V12Rogue52(7)).Character.Id;

        var success = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        Assert.Empty(success.Notes);
        Assert.Equal(0, success.Outcome.Adjusted);

        var failure = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Empty(failure.Notes);
        Assert.Equal(10, failure.Outcome.Adjusted);
    }

    [Fact]
    public void Evasion_does_nothing_while_incapacitated()
    {
        using var temp = new TempApp();
        var evasion = V12(410110, RulesFamilies.Srd521);
        var rogue = V12Rogue52(7);
        Assert.All(V12Pack521.Revisions.Single(r => r.Name == "Evasion").Effects.OfType<ResponseEffect>(), e => Assert.Equal("incapacitated", e.UnlessCondition));

        var id = temp.App.SaveCharacter(rogue with { Play = rogue.Play with { Conditions = ["incapacitated"] } }).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;
        Assert.All(sheet.Responses!.Where(r => r.ResponseId.StartsWith("evasion", StringComparison.Ordinal)), r =>
        {
            Assert.False(r.Available);
            Assert.Equal("Not while incapacitated", r.UnavailableReason);
        });

        var success = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionSuccess")], ResponseSource.Save, new DamageSave("dex", true)));
        // A save for half damage still halves on a success; Evasion's "none" is not applied.
        Assert.Equal(10, success.Outcome.Adjusted);
        var failure = temp.App.PreviewDamage(new(id, 20, "fire", [new(evasion.ContentId, "evasionFailure")], ResponseSource.Save, new DamageSave("dex", false)));
        Assert.Equal(20, failure.Outcome.Adjusted);
    }

    [Fact]
    public void Reliable_talent_52_floors_a_proficient_check_at_10()
    {
        using var temp = new TempApp();
        Assert.Null(temp.App.Roll(new RollCommand(temp.App.SaveCharacter(V12Rogue52(6)).Character.Id, Field: FieldIds.Skill("stealth"))).Floor);

        var id = temp.App.SaveCharacter(V12Rogue52(7)).Character.Id;
        var stealth = temp.App.Roll(new RollCommand(id, Field: FieldIds.Skill("stealth")));
        Assert.Equal(10, stealth.Floor!.Face);
        Assert.Equal(["Reliable Talent"], stealth.Floor.Reasons.Select(r => r.Source));
        Assert.Null(temp.App.Roll(new RollCommand(id, Field: FieldIds.Skill("athletics"))).Floor);
    }

    [Fact]
    public void Elusive_52_prevents_advantage_against_unless_incapacitated()
    {
        using var temp = new TempApp();
        Assert.Null(Sheet52(temp, 17).AttacksAgainst);

        var reason = Assert.Single(Sheet52(temp, 18).AttacksAgainst!.Reasons, r => r.Source == "Elusive");
        Assert.Equal((RollMode.Advantage, true), (reason.Mode, reason.Prevent));

        var incapacitated = V12Rogue52(18);
        var sheet = temp.App.SaveCharacter(incapacitated with { Play = incapacitated.Play with { Conditions = ["incapacitated"] } }).Sheet;
        Assert.DoesNotContain(sheet.AttacksAgainst?.Reasons ?? [], r => r.Source == "Elusive" && r.Prevent);
    }

    [Fact]
    public void The_v12_Rogue_52_keeps_every_unchanged_grant_and_adds_the_new_Thief_beside_the_old()
    {
        var old = RoguePack(RulesFamilies.Srd521).Revisions.Single(r => r.Kind == ContentKind.Class);
        var revised = V12Pack521.Revisions.Single(r => r.Kind == ContentKind.Class);
        var changed = new[] { 410102L, 410106, 410109, 410110, 410111, 410115 }.Select(n => V12(n, RulesFamilies.Srd521).ContentId).ToHashSet();
        IEnumerable<(string, ContentReference)> Grants(ContentRevision r) => r.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => (g.Id, g.Content!));
        var oldGrants = Grants(old).ToDictionary(g => g.Item1, g => g.Item2);
        foreach (var (id, content) in Grants(revised))
            Assert.Equal(changed.Contains(content.ContentId) ? V12(int.Parse(content.ContentId.ToString()[^6..]), RulesFamilies.Srd521) : oldGrants[id], content);
        Assert.Equal(oldGrants.Count, Grants(revised).Count());

        var choice = revised.Effects.OfType<ChoiceEffect>().Single(c => c.ChoiceId == "rogue-subclass");
        Assert.Equal([NewThief52, Thief(RulesFamilies.Srd521)], choice.Options);

        var oldThief = RoguePack(RulesFamilies.Srd521).Revisions.Single(r => r.Kind == ContentKind.Subclass);
        var newThief = V12Pack521.Revisions.Single(r => r.Kind == ContentKind.Subclass);
        var oldThiefGrants = Grants(oldThief).ToDictionary(g => g.Item1, g => g.Item2);
        foreach (var (id, content) in Grants(newThief))
            Assert.Equal(id == "second-story-work" ? V12(410502, RulesFamilies.Srd521) : oldThiefGrants[id], content);
        Assert.Equal(oldThiefGrants.Count, Grants(newThief).Count());
    }

    // ---- C2-6: existing characters on the older 5.2.1 Rogue ------------------------------------------------------------------------

    [Fact]
    public void A_C2_2_Rogue_52_calculates_as_before_and_is_offered_the_v12_class_and_Thief()
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd521, "Shortsword");
        var id = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd521, 18, [new(sword, Equipped: true)])).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        AssertClean(sheet);
        Assert.DoesNotContain(AttackOf(sheet, sword).Riders ?? [], r => r.OncePerTurn);
        Assert.Null(sheet.AttacksAgainst);
        Assert.Equal(0, sheet.Field(FieldIds.ClimbSpeed).Value);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.Single(f => f.Name == "Evasion").Automation);

        var offers = temp.App.AvailableUpdates(id);
        var offer = Assert.Single(offers, o => o.Role == ReferenceRole.Class && o.Name == "Rogue");
        Assert.Equal((RogueClass(RulesFamilies.Srd521), NewClass52, true), (offer.From, offer.To, offer.Bundled));
        Assert.Contains(offers, o => o.From == Thief(RulesFamilies.Srd521) && o.To == NewThief52);
    }

    [Fact]
    public void After_the_class_and_Thief_updates_the_52_v12_effects_apply()
    {
        using var temp = new TempApp();
        var sword = Weapon(RulesFamilies.Srd521, "Shortsword");
        var id = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd521, 18, [new(sword, Equipped: true)])).Character.Id;

        var updated = temp.App.ApplyUpdate(id, RogueClass(RulesFamilies.Srd521), NewClass52, confirm: true);
        AssertClean(updated.Sheet);
        Assert.All(updated.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
        Assert.Equal(NewClass52, updated.Character.Classes[0].Class);
        Assert.Equal("9d6", AttackOf(updated.Sheet, sword).Riders!.Single(r => r.OncePerTurn).Dice);
        Assert.Contains(updated.Sheet.AttacksAgainst!.Reasons, r => r.Source == "Elusive" && r.Prevent);
        Assert.Equal(0, updated.Sheet.Field(FieldIds.ClimbSpeed).Value);

        var thief = temp.App.ApplyUpdate(id, Thief(RulesFamilies.Srd521), NewThief52, confirm: true);
        AssertClean(thief.Sheet);
        Assert.Equal(30, thief.Sheet.Field(FieldIds.ClimbSpeed).Value);
        Assert.Contains(thief.Sheet.Active!, r => r == NewThief52);
    }

    [Fact]
    public void The_Thief_update_first_keeps_the_Thief_resolved_and_the_class_update_after_it_is_clean()
    {
        using var temp = new TempApp();
        var id = temp.App.SaveCharacter(Rogue(RulesFamilies.Srd521, 18)).Character.Id;

        // The Thief first, while the character is still on the old class (whose subclass choice lists only the old Thief).
        var viaThief = temp.App.ApplyUpdate(id, Thief(RulesFamilies.Srd521), NewThief52, confirm: true);
        Assert.Equal(RogueClass(RulesFamilies.Srd521), viaThief.Character.Classes[0].Class);
        Assert.DoesNotContain(viaThief.Sheet.Diagnostics, d => d.Code == "choice.invalid-option");
        AssertClean(viaThief.Sheet);
        Assert.True(viaThief.Sheet.Choices!.Single(c => c.ChoiceId == SubclassChoice(RulesFamilies.Srd521)).Resolved);
        Assert.Equal(30, viaThief.Sheet.Field(FieldIds.ClimbSpeed).Value);

        var both = temp.App.ApplyUpdate(id, RogueClass(RulesFamilies.Srd521), NewClass52, confirm: true);
        AssertClean(both.Sheet);
        Assert.Contains(both.Sheet.Active!, r => r == NewThief52);
        Assert.Equal(30, both.Sheet.Field(FieldIds.ClimbSpeed).Value);
    }
}
