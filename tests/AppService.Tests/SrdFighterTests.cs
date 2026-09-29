using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2.2 on the bundled SRD content (srd-&lt;family&gt;-fighter.json, srd-&lt;family&gt;-armor.json): a Fighter of each family,
/// levels 1–20, side by side. Values are the SRD tables' (docs/licensing/srd-pack-review.md, "M2.2 extension").
/// </summary>
public class SrdFighterTests
{
    private static ContentPack Fighters(string family) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-fighter.json");

    private static ContentPack Armor(string family) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-armor.json");

    private static ContentReference Named(ContentPack pack, string name) => pack.Revisions.Last(r => r.Name == name).Reference;

    private static readonly string[] Families = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    /// <summary>The level the Champion's Additional Fighting Style comes at: 10th (SRD 5.1 p. 25) or 7 (SRD 5.2.1 p. 49).</summary>
    private static int SecondStyleLevel(string family) => family == RulesFamilies.Srd51 ? 10 : 7;

    /// <summary>
    /// Str 16, Dex 14, Con 14 (+2): a Fighter with the Champion, Defense, and Archery as the second style, the choices
    /// answered (<paramref name="secondStyle"/> false leaves the second style open).
    /// </summary>
    private static Character Fighter(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, bool secondStyle = true)
    {
        var pack = Fighters(family);
        var fighter = Named(pack, "Fighter");
        var defense = Named(pack, family == RulesFamilies.Srd51 ? "Fighting Style: Defense" : "Defense");
        var skills = pack.Revisions.Where(r => r.Name.StartsWith("Fighter Skill:", StringComparison.Ordinal)).Take(2).Select(r => r.Reference).ToList();
        List<ChoiceSelection> choices =
        [
            new(fighter, "fighter-skills", skills),
            new(Named(pack, "Fighting Style"), "fighting-style", [defense]),
        ];
        if (level >= 3)
            choices.Add(new(fighter, "fighter-subclass", [Named(pack, "Champion")]));
        if (secondStyle && level >= SecondStyleLevel(family))
            choices.Add(new(Named(pack, "Additional Fighting Style"), "additional-fighting-style", [Named(pack, family == RulesFamilies.Srd51 ? "Fighting Style: Archery" : "Archery")]));
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD Fighter", RulesFamily = family, Level = level,
            Classes = [new(fighter, level)],
            BaseAbilities = scores ?? new(16, 14, 14, 10, 12, 8),
            Choices = choices,
            Equipment = equipment ?? [],
        };
    }

    private static int? Resource(CharacterSheet sheet, string label) => sheet.Resources!.SingleOrDefault(r => r.Label == label)?.Maximum;

    [Theory]
    // level, Second Wind (5.1, 5.2.1), Action Surge, Indomitable, attacks, critical range
    [InlineData(1, 1, 2, null, null, 1, 20)]
    [InlineData(2, 1, 2, 1, null, 1, 20)]
    [InlineData(3, 1, 2, 1, null, 1, 19)]
    [InlineData(4, 1, 3, 1, null, 1, 19)]
    [InlineData(5, 1, 3, 1, null, 2, 19)]
    [InlineData(9, 1, 3, 1, 1, 2, 19)]
    [InlineData(10, 1, 4, 1, 1, 2, 19)]
    [InlineData(11, 1, 4, 1, 1, 3, 19)]
    [InlineData(13, 1, 4, 1, 2, 3, 19)]
    [InlineData(15, 1, 4, 1, 2, 3, 18)]
    [InlineData(17, 1, 4, 2, 3, 3, 18)]
    [InlineData(20, 1, 4, 2, 3, 4, 18)]
    public void A_Fighter_follows_its_tables_in_both_families_side_by_side(int level, int wind51, int wind521, int? surge, int? indomitable, int attacks, int critical)
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Fighter(family, level)).Sheet;

            Assert.Equal(family == RulesFamilies.Srd51 ? wind51 : wind521, Resource(sheet, "Second Wind"));
            Assert.Equal(surge, Resource(sheet, "Action Surge"));
            Assert.Equal(indomitable, Resource(sheet, "Indomitable"));
            Assert.Equal(attacks, sheet.Field(FieldIds.Attacks).Value);
            Assert.Equal(critical, sheet.Field(FieldIds.CriticalRange).Value);
            // d10: 10 + Con 2 at level 1, then 6 + 2 per level (SRD fixed value).
            Assert.Equal(12 + ((level - 1) * 8), sheet.Field(FieldIds.HitPoints).Value);
            Assert.Equal(AutomationStatus.Automatic, sheet.Field(FieldIds.Attacks).Automation);
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "choice.unresolved" or "content.missing" or "effect.invalid-formula");
        }
    }

    [Fact]
    public void The_Fighter_and_the_Champion_are_offered_in_both_families()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var content = temp.App.ListContent(family);
            Assert.Single(content, o => o.Kind == ContentKind.Class && o.Name == "Fighter" && o.Compatible && !o.Superseded);
            Assert.Contains(content, o => o.Kind == ContentKind.Subclass && o.Name == "Champion" && o.Compatible);
            Assert.Equal(13, content.Count(o => o.Kind == ContentKind.Item && o.Compatible && temp.App.Store.FindRevision(o.Reference)!.Effects.OfType<ArmorEffect>().Any() && o.SourceTitle.StartsWith("System Reference", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Saving_throws_skills_and_weapons_come_from_the_starting_class()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Fighter(family, 1)).Sheet;
            // Str +3 and PB 2; Con +2 and PB 2; Dex without proficiency.
            Assert.Equal((5, 4, 2), (sheet.Field(FieldIds.Save(Ability.Str)).Value, sheet.Field(FieldIds.Save(Ability.Con)).Value, sheet.Field(FieldIds.Save(Ability.Dex)).Value));
            Assert.Equal(2, sheet.Choices!.Single(c => c.ChoiceId == "fighter-skills").Count);
        }
        Assert.Equal(8, Fighters(RulesFamilies.Srd51).Revisions.Count(r => r.Name.StartsWith("Fighter Skill:", StringComparison.Ordinal)));
        Assert.Equal(9, Fighters(RulesFamilies.Srd521).Revisions.Count(r => r.Name.StartsWith("Fighter Skill:", StringComparison.Ordinal))); // adds Persuasion
    }

    [Fact]
    public void The_subclass_and_the_second_fighting_style_are_offered_at_each_familys_level()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var level = SecondStyleLevel(family);
            Assert.DoesNotContain(temp.App.SaveCharacter(Fighter(family, 2)).Sheet.Choices!, c => c.ChoiceId == "fighter-subclass");
            Assert.Contains(temp.App.SaveCharacter(Fighter(family, 3)).Sheet.Choices!, c => c.ChoiceId == "fighter-subclass" && c.Resolved);
            Assert.DoesNotContain(temp.App.SaveCharacter(Fighter(family, level - 1)).Sheet.Choices!, c => c.ChoiceId == "additional-fighting-style");
            var second = temp.App.SaveCharacter(Fighter(family, level, secondStyle: false)).Sheet.Choices!.Single(c => c.ChoiceId == "additional-fighting-style");
            Assert.False(second.Resolved);
            Assert.Equal(family == RulesFamilies.Srd51 ? 6 : 4, second.Options.Count);
        }
    }

    [Fact]
    public void The_same_fighting_style_cannot_be_chosen_twice()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var pack = Fighters(family);
            var champion = pack.Revisions.Last(r => r.Name == "Additional Fighting Style").Reference;
            var defense = Named(pack, family == RulesFamilies.Srd51 ? "Fighting Style: Defense" : "Defense");
            var character = temp.App.SaveCharacter(Fighter(family, 10, secondStyle: false)).Character;
            var problem = Assert.Throws<AppValidationException>(() => temp.App.Choose(new(character.Id, champion, "additional-fighting-style", [defense]))).Problems;
            Assert.Contains(problem, p => p.Code == "choice.option-already-chosen");
        }
    }

    [Fact]
    public void Chain_mail_a_shield_and_the_defense_style_give_19_with_the_armor_warnings()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var armor = Armor(family);
            var chain = armor.Revisions.Single(r => r.Name is "Chain mail" or "Chain Mail").Reference;
            var shield = armor.Revisions.Single(r => r.Name == "Shield").Reference;
            var sheet = temp.App.SaveCharacter(Fighter(family, 1, [new(chain, Equipped: true), new(shield, Equipped: true)])).Sheet;

            var ac = sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(16 + 2 + 1, ac.Value); // chain mail, shield, Defense
            Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-strength" or "equipment.armor-untrained" or "equipment.shield-untrained"); // Str 16, trained
            Assert.Contains(sheet.Field(FieldIds.Skill(CharacterCalculator.Stealth)).Warnings, w => w.Code == "equipment.stealth-disadvantage");

            var weak = temp.App.SaveCharacter(Fighter(family, 1, [new(chain, Equipped: true)], new(12, 14, 14, 10, 12, 8))).Sheet;
            Assert.Contains(weak.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-strength" && w.Message.Contains("needs Strength 13", StringComparison.Ordinal));

            // Without armor, Defense does not apply (10 + Dex 2).
            Assert.Equal(12, temp.App.SaveCharacter(Fighter(family, 1)).Sheet.Field(FieldIds.ArmorClass).Value);
        }
    }

    [Fact]
    public void Second_wind_rolls_1d10_plus_the_fighter_level_and_the_2024_version_regains_one_use_on_a_short_rest()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var character = temp.App.SaveCharacter(Fighter(family, 6)).Character;
            var sheet = temp.App.GetCharacter(character.Id).Sheet;
            var feature = sheet.Features!.Single(f => f.Name == "Second Wind");
            var heal = feature.Effects.Single(e => e.Id == "second-wind-heal");
            Assert.Equal(("1d10", 6, Activation.BonusAction), (heal.Dice, heal.Bonus, heal.Activation));

            var record = temp.App.Roll(new(character.Id, feature.Content, "second-wind-heal"));
            Assert.Contains(record.Modifiers, m => m.Amount == 6);
            Assert.InRange(record.Total, 7, 16);

            var wind = sheet.Resources!.Single(r => r.Label == "Second Wind");
            var shortRest = wind.Recoveries.Single(r => r.On == RestPeriod.ShortRest);
            Assert.Equal(family == RulesFamilies.Srd51 ? (true, (int?)null) : (false, 1), (shortRest.All, shortRest.Value));
        }
    }

    [Fact]
    public void The_Champions_critical_range_shows_on_weapon_attack_rolls()
    {
        using var temp = new TempApp();
        var equipment = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-equipment.json");
        var longsword = equipment.Revisions.Single(r => r.Name == "Longsword").Reference;
        var character = temp.App.SaveCharacter(Fighter(RulesFamilies.Srd521, 3, [new(longsword, Equipped: true)])).Character;

        var record = temp.App.Roll(new(character.Id, Weapon: longsword));
        Assert.Contains("critical hit on 19–20", record.Provenance!.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void The_multiclass_prerequisite_is_strength_or_dexterity_13_and_heavy_armor_comes_only_with_the_starting_class()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var classes = TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-classes.json");
            // The v8 Wizard records that it gives no armor training (armor.none), so the Fighter's missing heavy armor shows.
            var wizard = classes.Revisions.Last(r => r.Name == "Wizard" && r.Kind == ContentKind.Class).Reference;
            var fighter = Named(Fighters(family), "Fighter");
            var plate = Armor(family).Revisions.Single(r => r.Name is "Plate" or "Plate Armor").Reference;
            Character Later(AbilityScores scores) => new()
            {
                Id = Guid.NewGuid(), Name = "Test Wizard Fighter", RulesFamily = family, Level = 2,
                Classes = [new(wizard, 1), new(fighter, 1)], BaseAbilities = scores, Equipment = [new(plate, Equipped: true)],
            };

            var dexterous = temp.App.SaveCharacter(Later(new(10, 13, 12, 16, 10, 10))).Sheet;
            Assert.DoesNotContain(dexterous.Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == fighter);
            Assert.Contains(dexterous.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained" && w.Message.Contains("heavy armor", StringComparison.Ordinal));

            var weak = temp.App.SaveCharacter(Later(new(10, 12, 12, 16, 10, 10))).Sheet;
            Assert.Contains(weak.Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == fighter);
        }
    }

    private static ContentPack Classes(string family) => TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-classes.json");

    [Fact]
    public void A_Paladin_who_took_a_level_of_Fighter_is_trained_for_plate()
    {
        // PR #12 review: the Paladin's heavy armor comes with the starting class, so a later Fighter level lacking heavy
        // armor must not flag the Plate. Both families' v8 Paladins record their armor training.
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var paladin = Classes(family).Revisions.Last(r => r.Name == "Paladin" && r.Kind == ContentKind.Class);
            Assert.Contains(paladin.Effects.OfType<GrantEffect>(), g => g.Target == "armor.heavy" && g.OnlyAs == ClassEntry.StartingClass);
            // The armor grants are a new revision; the v8 revision already on the PR branch is unchanged (insert-only).
            Assert.Equal(2, Classes(family).Revisions.Count(r => r.Name == "Paladin" && r.Kind == ContentKind.Class && r.SchemaVersion == 8));
            var plate = Armor(family).Revisions.Single(r => r.Name is "Plate" or "Plate Armor").Reference;
            var sheet = temp.App.SaveCharacter(new Character
            {
                Id = Guid.NewGuid(), Name = "Test Paladin Fighter", RulesFamily = family, Level = 6,
                Classes = [new(paladin.Reference, 5), new(Named(Fighters(family), "Fighter"), 1)],
                BaseAbilities = new(15, 10, 14, 10, 10, 13), Equipment = [new(plate, Equipped: true)],
            }).Sheet;

            var ac = sheet.Field(FieldIds.ArmorClass);
            Assert.Equal(18, ac.Value);
            Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.armor-strength");
        }
    }

    [Fact]
    public void A_2024_Cleric_with_a_feat_granting_only_light_armor_keeps_the_shield()
    {
        // PR #12 review: under 5.2.1 an untrained shield adds nothing. The Cleric revision records no armor training, so
        // a homebrew v8 feat granting light armor alone must not turn the check on and take the shield away.
        using var temp = new TempApp();
        var source = Guid.Parse("7c000000-0000-4000-8000-000000000a01");
        temp.App.Store.UpsertSource(new SourceRecord
        {
            Id = source, Title = "Test Feats", Publisher = "Me", RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = false,
        });
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Lightly Armored",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(source), Status = RevisionStatus.Published,
            Effects = [new GrantEffect { Id = "light", Grant = GrantKind.Proficiency, Target = "armor.light" }],
        };
        temp.App.Store.AddRevision(feat);
        var cleric = Classes(RulesFamilies.Srd521).Revisions.Last(r => r.Name == "Cleric" && r.Kind == ContentKind.Class).Reference;
        var shield = Armor(RulesFamilies.Srd521).Revisions.Single(r => r.Name == "Shield").Reference;

        var ac = temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Cleric", RulesFamily = RulesFamilies.Srd521, Level = 1, Classes = [new(cleric, 1)],
            Pins = [feat.Reference], BaseAbilities = new(10, 14, 12, 10, 16, 10), Equipment = [new(shield, Equipped: true)],
        }).Sheet.Field(FieldIds.ArmorClass);

        Assert.Equal(10 + 2 + 2, ac.Value);
        Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.shield-untrained" or "equipment.armor-untrained");
    }

    [Fact]
    public void The_new_Paladin_and_Ranger_revisions_count_their_extra_attack()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var classes = TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-classes.json");
            foreach (var name in new[] { "Paladin", "Ranger" })
            {
                var revisions = classes.Revisions.Where(r => r.Name == name && r.Kind == ContentKind.Class).ToList();
                Character At(ContentReference cls, int level) => new()
                {
                    Id = Guid.NewGuid(), Name = $"Test {name}", RulesFamily = family, Level = level, Classes = [new(cls, level)], BaseAbilities = new(15, 14, 13, 10, 14, 14),
                };
                Assert.Equal((1, 2), (temp.App.SaveCharacter(At(revisions[^1].Reference, 4)).Sheet.Field(FieldIds.Attacks).Value, temp.App.SaveCharacter(At(revisions[^1].Reference, 5)).Sheet.Field(FieldIds.Attacks).Value));
                // The pre-v8 revision (pinned by existing characters) keeps its reference-only Extra Attack until the update.
                Assert.Equal(1, temp.App.SaveCharacter(At(revisions.Last(r => r.SchemaVersion < 8).Reference, 5)).Sheet.Field(FieldIds.Attacks).Value);
            }
        }
    }
}
