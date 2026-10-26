using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Track 1 (D60) on the bundled SRD 5.2.1 content (srd-5.2.1-origins.json, srd-5.2.1-feats.json): every species,
/// background and feat of SRD 5.2.1 pp. 83–88 calculates as encoded. Values are the SRD's
/// (docs/licensing/srd-pack-review.md, "SRD 5.2.1 species, backgrounds and feats").
/// </summary>
public class SrdOrigins521Tests
{
    private static readonly ContentPack Origins = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-origins.json");
    private static readonly ContentPack Feats = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-feats.json");
    private static readonly ContentPack Base = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1.json");
    private static readonly ContentPack Fighters = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-fighter.json");

    private static ContentRevision Revision(string name) => Origins.Revisions.Concat(Feats.Revisions).Concat(Base.Revisions).Single(r => r.Name == name);

    private static ContentReference Named(string name) => Revision(name).Reference;

    private static CharacterSheet Sheet(TempApp temp, string fixture, int level = 1)
    {
        var character = TempApp.LoadFixture<Character>($"characters/{fixture}");
        // A new id per save: the same fixture saved twice in one data folder would be a stale edit.
        var champion = new ChoiceSelection(character.Classes[0].Class, "fighter-subclass", [Fighters.Revisions.Single(r => r.Name == "Champion").Reference]);
        return temp.App.SaveCharacter(character with
        {
            Id = Guid.NewGuid(), Level = level, Classes = [character.Classes[0] with { Level = level }],
            Choices = level >= 3 ? [.. character.Choices, champion] : character.Choices,
        }).Sheet;
    }

    /// <summary>A Fighter (Str 15, Dex 13, Con 14 unless <paramref name="scores"/>) with <paramref name="pins"/> and <paramref name="choices"/>.</summary>
    private static Character Character(IReadOnlyList<ContentReference> pins, IReadOnlyList<ChoiceSelection> choices, AbilityScores? scores = null, int level = 1)
    {
        var fighter = Fighters.Revisions.Single(r => r.Name == "Fighter").Reference;
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD 5.2.1 origin", RulesFamily = RulesFamilies.Srd521, Level = level,
            Classes = [new(fighter, level)],
            BaseAbilities = scores ?? new(15, 13, 14, 10, 12, 8),
            Pins = pins,
            Choices =
            [
                new(fighter, "fighter-skills", [Fighters.Revisions.Single(r => r.Name == "Fighter Skill: Acrobatics").Reference, Fighters.Revisions.Single(r => r.Name == "Fighter Skill: Animal Handling").Reference]),
                new(Fighters.Revisions.Single(r => r.Name == "Fighting Style").Reference, "fighting-style", [Fighters.Revisions.Single(r => r.Name == "Defense").Reference]),
                .. level >= 3 ? [new ChoiceSelection(fighter, "fighter-subclass", [Fighters.Revisions.Single(r => r.Name == "Champion").Reference])] : Array.Empty<ChoiceSelection>(),
                .. choices,
            ],
        };
    }

    private static int? Resource(CharacterSheet sheet, string label) => sheet.Resources!.SingleOrDefault(r => r.Label == label)?.Maximum;

    private static bool Proficient(CharacterSheet sheet, string skill) => sheet.Field(FieldIds.Skill(skill)).Mark != ProficiencyMark.None;

    private static IEnumerable<string> Resistances(CharacterSheet sheet) =>
        (sheet.DamageDefences ?? []).Where(d => d.Kind == DamageDefenceKind.Resistance).Select(d => d.Type);

    private static void Clean(CharacterSheet sheet)
    {
        Assert.True(sheet.Diagnostics.Count == 0, string.Join("; ", sheet.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{c.SourceName}: {c.ChoiceId}"));
    }

    [Theory]
    // fixture, species, speed (SRD 5.2.1 pp. 84–86; the Wood Elf lineage raises the Elf's 30 to 35)
    [InlineData("srd521-dragonborn-l1.json", "Dragonborn", 30)]
    [InlineData("srd521-elf-l1.json", "Elf", 35)]
    [InlineData("srd521-gnome-l1.json", "Gnome", 30)]
    [InlineData("srd521-goliath-l1.json", "Goliath", 35)]
    [InlineData("srd521-human-l1.json", "Human", 30)]
    [InlineData("srd521-orc-l1.json", "Orc", 30)]
    [InlineData("srd521-tiefling-l1.json", "Tiefling", 30)]
    public void Each_species_fixture_calculates_with_its_speed_and_no_open_choice(string fixture, string species, int speed)
    {
        using var temp = new TempApp();
        var sheet = Sheet(temp, fixture);

        Clean(sheet);
        Assert.Contains(Named(species), sheet.Active!);
        Assert.Equal(speed, sheet.Field(FieldIds.Speed).Value);
    }

    [Theory]
    // Size is reference text (no size field exists); the summary states it as printed.
    [InlineData("Dragonborn", "Size: Medium (about 5–7 feet tall)")]
    [InlineData("Elf", "Size: Medium (about 5–6 feet tall)")]
    [InlineData("Gnome", "Size: Small (about 3–4 feet tall)")]
    [InlineData("Goliath", "Size: Medium (about 7–8 feet tall)")]
    [InlineData("Halfling", "Size: Small (about 2–3 feet tall)")]
    [InlineData("Human", "Size: Medium (about 4–7 feet tall) or Small (about 2–4 feet tall), chosen when you select this species")]
    [InlineData("Orc", "Size: Medium (about 6–7 feet tall)")]
    [InlineData("Tiefling", "Size: Medium (about 4–7 feet tall) or Small (about 3–4 feet tall), chosen when you select this species")]
    public void Each_species_states_its_size_as_printed(string species, string size) =>
        Assert.StartsWith($"Creature Type: Humanoid. {size}.", Revision(species).Summary, StringComparison.Ordinal);

    [Fact]
    public void A_Dragonborn_resists_its_ancestors_damage_and_breathes_PB_times_with_flight_from_level_5()
    {
        using var temp = new TempApp();
        var first = Sheet(temp, "srd521-dragonborn-l1.json");
        var fifth = Sheet(temp, "srd521-dragonborn-l1.json", 5);

        Assert.Equal(["fire"], Resistances(first)); // Red: Fire (Draconic Ancestors table, p. 84)
        Assert.Equal((2, 3), (Resource(first, "Breath Weapon"), Resource(fifth, "Breath Weapon")));
        var breath = sheet(first).Single(e => e.Id == "breath-weapon-damage");
        Assert.Equal(("1d10", "breath-weapon"), (breath.Dice, breath.ResourceId));
        Assert.Equal((null, 1), (Resource(first, "Draconic Flight"), Resource(fifth, "Draconic Flight")));

        static IEnumerable<FeatureEffect> sheet(CharacterSheet s) => s.Features!.Single(f => f.Name == "Dragonborn").Effects;
    }

    [Fact]
    public void Every_Draconic_Ancestry_resists_the_type_the_table_prints()
    {
        string[] expected = ["acid", "lightning", "fire", "lightning", "acid", "fire", "poison", "fire", "cold", "cold"];
        var ancestry = Revision("Dragonborn").Effects.OfType<ChoiceEffect>().Single(c => c.ChoiceId == "draconic-ancestry");

        Assert.Equal(expected, ancestry.Options.Select(o => Origins.Revisions.Single(r => r.Reference == o).Effects.OfType<GrantEffect>().Single().DamageType));
    }

    [Fact]
    public void An_Elf_gains_the_Keen_Senses_skill_and_a_Wood_Elf_moves_35_feet()
    {
        using var temp = new TempApp();
        var sheet = Sheet(temp, "srd521-elf-l1.json");

        Assert.True(Proficient(sheet, "perception"));
        Assert.Equal(3, Revision("Elf").Effects.OfType<ChoiceEffect>().Single(c => c.ChoiceId == "keen-senses").Options.Count);
        Assert.Contains(Named("Elven Lineage: Wood Elf"), sheet.Active!);
    }

    [Fact]
    public void A_Forest_Gnome_casts_Speak_with_Animals_PB_times()
    {
        using var temp = new TempApp();

        Assert.Equal(2, Resource(Sheet(temp, "srd521-gnome-l1.json"), "Speak with Animals without a slot"));
    }

    [Fact]
    public void A_Goliath_has_PB_uses_of_its_Giant_Ancestry_and_Large_Form_from_level_5()
    {
        using var temp = new TempApp();
        var first = Sheet(temp, "srd521-goliath-l1.json");
        var fifth = Sheet(temp, "srd521-goliath-l1.json", 5);

        Assert.Equal(2, Resource(first, "Giant Ancestry"));
        var endurance = first.Features!.Single(f => f.Name == "Giant Ancestry: Stone’s Endurance").Effects.Single(e => e.Id == "stones-endurance");
        Assert.Equal(("1d12", 2), (endurance.Dice, endurance.Bonus)); // 1d12 + Con modifier (+2)
        Assert.Equal((null, 1), (Resource(first, "Large Form"), Resource(fifth, "Large Form")));
    }

    [Fact]
    public void A_Human_takes_a_skill_and_an_Origin_feat_whose_own_choice_applies()
    {
        using var temp = new TempApp();
        var sheet = Sheet(temp, "srd521-human-l1.json");

        Assert.Contains(Named("Skilled"), sheet.Active!);
        Assert.All(new[] { "stealth", "arcana", "history", "medicine" }, s => Assert.True(Proficient(sheet, s), s));
        // Versatile offers the four Origin feats (SRD 5.2.1 p. 87), Savage Attacker from the M1 pack among them.
        Assert.Equal(["Alert", "Magic Initiate", "Savage Attacker", "Skilled"],
            Revision("Human").Effects.OfType<ChoiceEffect>().Single(c => c.ChoiceId == "versatile").Options.Select(o => Origins.Revisions.Concat(Feats.Revisions).Concat(Base.Revisions).Single(r => r.Reference == o).Name));
        Assert.Equal(18, Revision("Human").Effects.OfType<ChoiceEffect>().Single(c => c.ChoiceId == "skillful").Options.Count);
    }

    [Fact]
    public void An_Orc_rushes_PB_times_and_endures_once()
    {
        using var temp = new TempApp();
        var sheet = Sheet(temp, "srd521-orc-l1.json");

        Assert.Equal((2, 1), (Resource(sheet, "Adrenaline Rush"), Resource(sheet, "Relentless Endurance")));
        var rush = sheet.Resources!.Single(r => r.Label == "Adrenaline Rush");
        Assert.Equal([RestPeriod.ShortRest, RestPeriod.LongRest], rush.Recoveries.Select(r => r.On));
    }

    [Theory]
    [InlineData("Fiendish Legacy: Abyssal", "poison")]
    [InlineData("Fiendish Legacy: Chthonic", "necrotic")]
    [InlineData("Fiendish Legacy: Infernal", "fire")]
    public void A_Tieflings_legacy_gives_its_resistance(string legacy, string type)
    {
        using var temp = new TempApp();
        var tiefling = Named("Tiefling");
        var sheet = temp.App.SaveCharacter(Character([tiefling], [new(tiefling, "fiendish-legacy", [Named(legacy)])])).Sheet;

        Clean(sheet);
        Assert.Equal([type], Resistances(sheet));
    }

    [Fact]
    public void A_Halfling_is_reference_text_at_speed_30()
    {
        using var temp = new TempApp();
        var sheet = temp.App.SaveCharacter(Character([Named("Halfling")], [])).Sheet;

        Clean(sheet);
        Assert.Equal(30, sheet.Field(FieldIds.Speed).Value);
        Assert.Empty(Revision("Halfling").Effects);
    }

    [Theory]
    // background, its first ability option, skills, feat (SRD 5.2.1 p. 83)
    [InlineData("Acolyte", "Acolyte Ability Scores: Intelligence +2, Wisdom +1", "insight", "religion", "Magic Initiate")]
    [InlineData("Criminal", "Criminal Ability Scores: Dexterity +2, Constitution +1", "sleightOfHand", "stealth", "Alert")]
    [InlineData("Sage", "Sage Ability Scores: Constitution +2, Intelligence +1", "arcana", "history", "Magic Initiate")]
    public void Each_background_grants_its_skills_and_feat_and_offers_seven_ability_options(string background, string option, string skill1, string skill2, string feat)
    {
        using var temp = new TempApp();
        var reference = Named(background);
        var choiceId = $"{background.ToLowerInvariant()}-ability-scores";
        var sheet = temp.App.SaveCharacter(Character([Named("Halfling"), reference], [new(reference, choiceId, [Named(option)])])).Sheet;

        Clean(sheet);
        Assert.True(Proficient(sheet, skill1), skill1);
        Assert.True(Proficient(sheet, skill2), skill2);
        Assert.Contains(Named(feat), sheet.Active!);
        Assert.Equal(7, Revision(background).Effects.OfType<ChoiceEffect>().Single().Options.Count);
        var bumped = Revision(option).Effects.OfType<ModifierEffect>().ToList();
        Assert.Equal((2, 1), (int.Parse(bumped[0].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(bumped[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Alert_adds_the_proficiency_bonus_to_initiative()
    {
        using var temp = new TempApp();
        var without = temp.App.SaveCharacter(Character([Named("Halfling")], [])).Sheet;
        var with = temp.App.SaveCharacter(Character([Named("Halfling"), Named("Alert")], [])).Sheet;

        Assert.Equal(without.Field(FieldIds.Initiative).Value + 2, with.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Ability_Score_Improvement_offers_every_plus_2_and_every_pair_of_plus_1s()
    {
        using var temp = new TempApp();
        var asi = Named("Ability Score Improvement");
        var sheet = temp.App.SaveCharacter(Character([Named("Halfling"), asi], [new(asi, "ability-score-improvement", [Named("Ability Score Improvement: Wisdom +1, Charisma +1")])], level: 4)).Sheet;

        Clean(sheet);
        Assert.Equal(21, Revision("Ability Score Improvement").Effects.OfType<ChoiceEffect>().Single().Options.Count); // 6 + 15 pairs
        Assert.Equal((13, 9), (sheet.Field(FieldIds.Score(Ability.Wis)).Value, sheet.Field(FieldIds.Score(Ability.Cha)).Value));
    }

    [Theory]
    // Strength or Dexterity 13+ (SRD 5.2.1 p. 87) is a restriction group: either one is enough.
    [InlineData(13, 12, true)]
    [InlineData(12, 13, true)]
    [InlineData(12, 12, false)]
    public void Grappler_requires_Strength_or_Dexterity_13(int str, int dex, bool applies)
    {
        using var temp = new TempApp();
        var grappler = Named("Grappler");
        var character = Character([Named("Halfling"), grappler], [new(grappler, "grappler-ability-score", [Named("Grappler: Dexterity +1")])], new(str, dex, 14, 10, 12, 8), level: 4);
        var sheet = temp.App.SaveCharacter(character).Sheet;

        Assert.Equal(applies, sheet.Active!.Contains(grappler));
        Assert.Equal(!applies, sheet.Diagnostics.Any(d => d.Code == "restriction.unmet"));
        Assert.Equal(applies ? dex + 1 : dex, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
    }

    [Fact]
    public void Each_feat_states_its_category_and_prerequisite_first()
    {
        var feats = Feats.Revisions.Concat(Base.Revisions).Concat(Fighters.Revisions).Where(r => r.Kind == ContentKind.Feat).ToList();

        Assert.Equal(17, feats.Count);
        Assert.Equal(4, feats.Count(f => f.Summary!.StartsWith("Origin Feat", StringComparison.Ordinal)));
        Assert.Equal(2, feats.Count(f => f.Summary!.StartsWith("General Feat (Prerequisite: Level 4+", StringComparison.Ordinal)));
        Assert.Equal(4, feats.Count(f => f.Summary!.StartsWith("Fighting Style Feat (Prerequisite: Fighting Style Feature)", StringComparison.Ordinal)));
        Assert.Equal(7, feats.Count(f => f.Summary!.StartsWith("Epic Boon Feat (Prerequisite: Level 19+", StringComparison.Ordinal)));
        // No level or "has a feature" prerequisite can be encoded: the text says so, and nothing pretends to check it.
        Assert.All(feats.Where(f => f.Summary!.Contains("Level ", StringComparison.Ordinal) && f.Summary.Contains("Prerequisite", StringComparison.Ordinal) && !f.Summary.StartsWith("Fighting Style", StringComparison.Ordinal)),
            f => Assert.Contains("TomeStack: the level", f.Summary!, StringComparison.Ordinal));
    }

    [Fact]
    public void Boon_of_Fate_is_once_per_short_or_long_rest_and_Magic_Initiate_once_per_long_rest()
    {
        using var temp = new TempApp();
        var sheet = temp.App.SaveCharacter(Character([Named("Halfling"), Named("Boon of Fate"), Named("Magic Initiate")], [])).Sheet;

        Assert.Equal([RestPeriod.ShortRest, RestPeriod.LongRest], sheet.Resources!.Single(r => r.Label == "Improve Fate").Recoveries.Select(r => r.On));
        Assert.Equal(1, Resource(sheet, "Magic Initiate: level 1 spell without a slot"));
    }

    [Fact]
    public void Every_new_revision_is_published_at_the_lowest_schema_version_it_needs()
    {
        var all = TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack).ToList();
        var catalog = new InMemoryContentCatalog([.. all.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], [.. all.SelectMany(p => p.Revisions)]);

        Assert.All(Origins.Revisions.Concat(Feats.Revisions), r => Assert.Equal((r.Name, ContentValidator.Validate(r, catalog).RequiredSchemaVersion), (r.Name, r.SchemaVersion)));
    }

    [Fact]
    public void New_names_are_unique_and_never_start_like_the_M1_species_or_background()
    {
        // The e2e flow picks the Dwarf and the Soldier by a name prefix (flow.e2e.tsx), and tests look content up by name.
        // The feat "Ability Score Improvement" shares its SRD name with the class feature; imports match by kind.
        var names = Origins.Revisions.Concat(Feats.Revisions).Select(r => r.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(names, n => n.StartsWith("Dwarf", StringComparison.Ordinal) || n.StartsWith("Soldier", StringComparison.Ordinal));
        Assert.Equal(["Ability Score Improvement"], names.Intersect(Base.Revisions.Concat(Fighters.Revisions).Select(r => r.Name)));
    }
}
