using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D04 multiclass prerequisites and "as a multiclass character" proficiency subsets, and SPEC C-02/C-04 weapons, attacks
/// and damage (content schema v5). Original fixtures: "Fixture Duelist" and the weapons in <c>fixture-pack-m2-combat.json</c>.
/// </summary>
public class MulticlassAndAttackTests
{
    private static Character With(AbilityScores scores, IReadOnlyList<EquipmentEntry>? equipment = null, params ClassLevel[] classes) =>
        Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            BaseAbilities = scores,
            Equipment = equipment ?? [],
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.AllCatalog());

    private static readonly AbilityScores Nimble = new(14, 16, 12, 10, 10, 10);

    [Fact]
    public void Saving_throws_and_the_full_skill_choice_come_only_with_the_starting_class()
    {
        var starting = Sheet(With(Nimble, null, new ClassLevel(Fixtures.Duelist, 1), new ClassLevel(Fixtures.Arcanist, 1)));
        var later = Sheet(With(Nimble, null, new ClassLevel(Fixtures.Arcanist, 1), new ClassLevel(Fixtures.Duelist, 1)));

        Assert.Equal(2 + 2, starting.Field(FieldIds.Save(Ability.Str)).Value); // Str +2 and PB 2
        Assert.Equal(2, later.Field(FieldIds.Save(Ability.Str)).Value); // no Duelist save proficiency as a later class
        Assert.Equal(["duelist-skills"], starting.Choices!.Where(c => c.Source == Fixtures.Duelist).Select(c => c.ChoiceId));
        Assert.Equal(["duelist-multiclass-skill"], later.Choices!.Where(c => c.Source == Fixtures.Duelist).Select(c => c.ChoiceId));
        Assert.Equal(1, later.Choices!.Single(c => c.ChoiceId == "duelist-multiclass-skill").Count);
    }

    [Fact]
    public void Multiclass_prerequisites_warn_only_when_multiclassed_and_either_alternative_is_enough()
    {
        var weak = new AbilityScores(10, 12, 12, 16, 10, 10);
        var single = Sheet(With(weak, null, new ClassLevel(Fixtures.Duelist, 3)));
        var multi = Sheet(With(weak, null, new ClassLevel(Fixtures.Duelist, 3), new ClassLevel(Fixtures.Arcanist, 1)));
        var dexterous = Sheet(With(weak with { Dex = 13 }, null, new ClassLevel(Fixtures.Duelist, 3), new ClassLevel(Fixtures.Arcanist, 1)));

        Assert.DoesNotContain(single.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
        var unmet = Assert.Single(multi.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
        Assert.Equal(Fixtures.Duelist, unmet.Content);
        Assert.Contains("requires Strength score 13 or Dexterity score 13 or higher; this character has Strength score 10, Dexterity score 12", unmet.Message, StringComparison.Ordinal);
        Assert.Contains(Fixtures.Duelist, multi.Active!); // the class stays applied: its levels are taken
        Assert.DoesNotContain(dexterous.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
    }

    [Fact]
    public void Equipped_weapons_give_attacks_with_to_hit_damage_and_traces()
    {
        var sheet = Sheet(With(
            Nimble,
            [new(Fixtures.Longblade, Equipped: true), new(Fixtures.Needle, Equipped: true), new(Fixtures.Slingbow, Equipped: true)],
            new ClassLevel(Fixtures.Duelist, 1)));

        // Str +2, Dex +3, PB 2, proficient with simple and martial weapons.
        Assert.Equal(
            [
                ("Fixture Longblade", Ability.Str, 4, "1d8+2", "1d10+2"),
                ("Fixture Needle", Ability.Dex, 5, "1d4+3", null),
                ("Fixture Slingbow", Ability.Dex, 5, "1d6+3", null),
            ],
            sheet.Attacks!.Select(a => (a.Name, a.Ability, a.ToHit, a.Damage, a.VersatileDamage)));
        var needle = sheet.Attacks!.Single(a => a.Name == "Fixture Needle");
        Assert.Contains("finesse", needle.Trace[0].Description, StringComparison.Ordinal);
        Assert.Equal(Fixtures.Duelist, needle.Trace[1].Origin.Content);
        Assert.Equal(("80/320", AutomationStatus.Automatic), (sheet.Attacks!.Single(a => a.Name == "Fixture Slingbow").Range, needle.Automation));
    }

    [Fact]
    public void Without_any_recorded_weapon_proficiency_the_attack_is_assisted_and_says_so()
    {
        var sheet = Sheet(With(Nimble, [new(Fixtures.Longblade, Equipped: true)], new ClassLevel(Fixtures.Arcanist, 1)));

        var attack = Assert.Single(sheet.Attacks!);
        Assert.Equal((2, false, AutomationStatus.Assisted), (attack.ToHit, attack.Proficient, attack.Automation));
        Assert.Equal("attack.proficiency-unknown", Assert.Single(attack.Warnings).Code);
        Assert.Empty(Sheet(With(Nimble, [new(Fixtures.Longblade, Equipped: false)], new ClassLevel(Fixtures.Arcanist, 1))).Attacks!); // unequipped
    }

    [Fact]
    public void Feature_rolls_carry_their_activation_for_grouping()
    {
        var sheet = Sheet(With(Nimble, null, new ClassLevel(Fixtures.Duelist, 2)));

        var rolls = sheet.Features!.SelectMany(f => f.Effects).Where(e => e.Type == "roll").ToDictionary(e => e.Label!, e => e.Activation);
        Assert.Equal(Activation.Reaction, rolls["Riposte damage"]);
        Assert.Equal(Activation.BonusAction, rolls["Second Breath healing"]);
    }

    [Fact]
    public void Validation_checks_weapons_and_refuses_v5_fields_in_an_older_revision()
    {
        var catalog = Fixtures.AllCatalog();
        var longblade = catalog.FindRevision(Fixtures.Longblade)!;
        var weapon = longblade.Effects.OfType<WeaponEffect>().Single();
        var badDice = longblade with { RevisionId = Guid.NewGuid(), Effects = [weapon with { Damage = "1d8+" }] };
        var duelist = catalog.FindRevision(Fixtures.Duelist)!;

        Assert.True(ContentValidator.Validate(longblade, catalog).CanPublish);
        Assert.True(ContentValidator.Validate(duelist, catalog).CanPublish);
        Assert.Contains(ContentValidator.Validate(badDice, catalog).Errors, e => e.Code == "validate.dice-invalid");
        Assert.Contains(ContentValidator.Validate(duelist with { RevisionId = Guid.NewGuid(), SchemaVersion = 4 }, catalog).Errors, e => e.Code == "validate.requires-v5");
    }

    [Fact]
    public void An_ordinary_prerequisite_group_is_met_by_either_alternative()
    {
        // A non-multiclass restriction group on a feat: Str 13 or Dex 13 (content v5 'group').
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = ContentKind.Feat,
            Name = "Test Either Feat",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(Fixtures.CombatPack().Sources[0].Id, new PageRef(1)),
            Status = RevisionStatus.Published,
            Effects =
            [
                new RestrictionEffect { Id = "str", Field = FieldIds.Score(Ability.Str), Minimum = 13, Group = "either" },
                new RestrictionEffect { Id = "dex", Field = FieldIds.Score(Ability.Dex), Minimum = 13, Group = "either" },
                new ModifierEffect { Id = "bonus", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
            ],
        };
        var withFeat = new InMemoryContentCatalog([.. Fixtures.CombatPack().Sources, .. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources], [.. Fixtures.M1Pack().Revisions, .. Fixtures.CombatPack().Revisions, feat]);
        Character Pinned(AbilityScores scores) => With(scores, null, new ClassLevel(Fixtures.Duelist, 1)) with { Pins = [feat.Reference] };

        var dexOnly = CharacterCalculator.Calculate(Pinned(new(10, 14, 10, 10, 10, 10)), withFeat);
        var neither = CharacterCalculator.Calculate(Pinned(new(10, 12, 10, 10, 10, 10)), withFeat);

        Assert.Contains(feat.Reference, dexOnly.Active!);
        Assert.Single(neither.Diagnostics, d => d.Code == "restriction.unmet" && d.Content == feat.Reference);
        Assert.DoesNotContain(feat.Reference, neither.Active!);
    }
}
