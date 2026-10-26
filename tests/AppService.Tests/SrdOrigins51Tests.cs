using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// The eight SRD 5.1 races of srd-5.1-origins.json (pp. 3-7), each through an invented level-1 Barbarian fixture
/// (tests/RulesFixtures/characters/srd51-&lt;race&gt;-l1.json, base scores 15/13/14/8/12/10, every choice answered).
/// Values are the SRD's (docs/licensing/srd-pack-review.md, "SRD 5.1 races").
/// </summary>
public class SrdOrigins51Tests
{
    private static readonly ContentPack Origins = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-origins.json");

    private static ContentReference Named(ContentPack pack, string name) => pack.Revisions.Single(r => r.Name == name).Reference;

    private static CharacterView Load(TempApp temp, string slug) =>
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>($"characters/srd51-{slug}-l1.json"));

    public static TheoryData<string, string, int, string, int[]> Races() => new()
    {
        // slug, race, speed, size, scores (Str, Dex, Con, Int, Wis, Cha) after the race and subrace increases
        { "dwarf", "Dwarf", 25, "Medium", [15, 13, 16, 8, 13, 10] },        // +2 Con, Hill Dwarf +1 Wis
        { "elf", "Elf", 30, "Medium", [15, 15, 14, 9, 12, 10] },            // +2 Dex, High Elf +1 Int
        { "halfling", "Halfling", 25, "Small", [15, 15, 14, 8, 12, 11] },   // +2 Dex, Lightfoot +1 Cha
        { "human", "Human", 30, "Medium", [16, 14, 15, 9, 13, 11] },        // +1 to each
        { "dragonborn", "Dragonborn", 30, "Medium", [17, 13, 14, 8, 12, 11] }, // +2 Str, +1 Cha
        { "gnome", "Gnome", 25, "Small", [15, 13, 15, 10, 12, 10] },        // +2 Int, Rock Gnome +1 Con
        { "half-elf", "Half-Elf", 30, "Medium", [16, 13, 15, 8, 12, 12] },  // +2 Cha, chosen +1 Str and +1 Con
        { "tiefling", "Tiefling", 30, "Medium", [15, 13, 14, 9, 12, 12] },  // +1 Int, +2 Cha
    };

    [Theory]
    [MemberData(nameof(Races))]
    public void Each_race_calculates_its_speed_size_and_ability_increases(string slug, string race, int speed, string size, int[] scores)
    {
        using var temp = new TempApp();
        var sheet = Load(temp, slug).Sheet;

        Assert.Equal(scores, Enum.GetValues<Ability>().Select(a => sheet.Field(FieldIds.Score(a)).Value));
        // 2014 rules: species content may raise ability scores (RulesFamilyPolicy.AbilityIncreaseSource), subraces included.
        // The refusal is a field warning on the score (never a sheet diagnostic).
        Assert.DoesNotContain(Enum.GetValues<Ability>().SelectMany(a => sheet.Field(FieldIds.Score(a)).Warnings), d => d.Code == "policy.ability-increase-source");

        var walking = sheet.Field(FieldIds.Speed);
        Assert.Equal(speed, walking.Value);
        Assert.Equal(race, walking.Trace.Last().Origin.ContentName);

        var revision = Origins.Revisions.Single(r => r.Name == race);
        Assert.Contains($"Your size is {size}.", revision.Summary, StringComparison.Ordinal);
        Assert.Equal(ContentKind.Species, revision.Kind);

        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{c.SourceName} {c.ChoiceId}"));
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "choice.unresolved" or "content.missing" or "effect.unknown-target" or "effect.invalid-formula");
    }

    [Theory]
    [InlineData("dwarf", "Dwarf", "Hill Dwarf")]
    [InlineData("elf", "Elf", "High Elf")]
    [InlineData("halfling", "Halfling", "Lightfoot")]
    [InlineData("gnome", "Gnome", "Rock Gnome")]
    public void A_race_with_a_subrace_offers_the_SRD_subrace_as_a_choice(string slug, string race, string subrace)
    {
        using var temp = new TempApp();
        var answered = Load(temp, slug);
        Assert.Contains(Named(Origins, subrace), answered.Sheet.Active!);

        var unanswered = temp.App.SaveCharacter(answered.Character with { Id = Guid.NewGuid(), Choices = [.. answered.Character.Choices.Where(c => c.Source != Named(Origins, race))] }).Sheet;
        var open = Assert.Single(unanswered.Choices!, c => c.SourceName == race);
        Assert.Equal((1, false), (open.Count, open.Resolved));
        Assert.Equal([Named(Origins, subrace)], open.Options);
        Assert.Contains(unanswered.Diagnostics, d => d.Code == "choice.unresolved");
        Assert.DoesNotContain(Named(Origins, subrace), unanswered.Active!);
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(3, 38)]
    public void Hill_dwarf_toughness_and_poison_resistance(int level, int hitPoints)
    {
        using var temp = new TempApp();
        var character = Load(temp, "dwarf").Character;
        var barbarian = character.Classes[0].Class;
        List<ChoiceSelection> choices = [.. character.Choices];
        if (level >= 3)
            choices.Add(new(barbarian, "primal-path", [new(Guid.Parse("51c00000-0000-4000-8000-000000000014"), Guid.Parse("51e00000-0000-4000-8000-000000000014"))]));
        var sheet = temp.App.SaveCharacter(character with { Level = level, Classes = [new(barbarian, level)], Choices = choices }).Sheet;

        // Barbarian d12: 12 + Con 3 (16 after +2) at 1st level, 7 + 3 per later level, plus Dwarven Toughness 1 per level.
        var hp = sheet.Field(FieldIds.HitPoints);
        Assert.Equal(hitPoints, hp.Value);
        Assert.Equal(level, hp.Trace.Single(t => t.Origin.ContentName == "Hill Dwarf").Amount);
        var poison = Assert.Single(sheet.DamageDefences!);
        Assert.Equal(("poison", DamageDefenceKind.Resistance), (poison.Type, poison.Kind));
    }

    [Theory]
    [InlineData("dwarf", "Warhammer", true)]
    [InlineData("elf", "Longbow", true)]
    [InlineData("human", "Longbow", false)]
    public void Racial_weapon_training_makes_the_attack_proficient(string slug, string weapon, bool proficient)
    {
        // The M1 Barbarian revision grants no weapon proficiencies, so only the race can.
        using var temp = new TempApp();
        var equipment = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-equipment.json");
        var character = Load(temp, slug).Character;
        var sheet = temp.App.SaveCharacter(character with { Equipment = [new(Named(equipment, weapon), Equipped: true)] }).Sheet;

        Assert.Equal(proficient, Assert.Single(sheet.Attacks!).Proficient);
    }

    [Fact]
    public void Known_gap_the_armor_strength_warning_does_not_know_the_dwarf_exemption()
    {
        // The SRD 5.1 Dwarf's speed "is not reduced by wearing heavy armor", but no content field can say so, so the
        // armor Strength warning still tells a Dwarf to lower Speed (docs/licensing/srd-pack-review.md, "SRD 5.1 races",
        // reference text). The calculated speed is not changed. When a field can express the exemption, invert this test.
        using var temp = new TempApp();
        var armor = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-armor.json");
        var character = Load(temp, "dwarf").Character;
        var sheet = temp.App.SaveCharacter(character with
        {
            BaseAbilities = new(12, 13, 14, 8, 12, 10),
            Equipment = [new(Named(armor, "Plate"), Equipped: true)],
        }).Sheet;

        Assert.Contains(sheet.Field(FieldIds.ArmorClass).Warnings, d => d.Code == "equipment.armor-strength");
        Assert.Equal(25, sheet.Field(FieldIds.Speed).Value);
    }

    [Theory]
    [InlineData("elf", "perception")]       // Keen Senses
    [InlineData("half-elf", "insight")]     // Skill Versatility (chosen)
    [InlineData("half-elf", "stealth")]
    public void Racial_skill_proficiencies_apply(string slug, string skill)
    {
        using var temp = new TempApp();
        var sheet = Load(temp, slug).Sheet;
        var ability = CharacterCalculator.Skills.Single(s => s.Key == skill).Ability;

        Assert.Equal(sheet.Field(FieldIds.Modifier(ability)).Value + 2, sheet.Field(FieldIds.Skill(skill)).Value);
    }

    [Theory]
    [InlineData("Black", "acid")]
    [InlineData("Blue", "lightning")]
    [InlineData("Brass", "fire")]
    [InlineData("Bronze", "lightning")]
    [InlineData("Copper", "acid")]
    [InlineData("Gold", "fire")]
    [InlineData("Green", "poison")]
    [InlineData("Red", "fire")]
    [InlineData("Silver", "cold")]
    [InlineData("White", "cold")]
    public void Draconic_ancestry_sets_the_damage_resistance(string dragon, string damage)
    {
        using var temp = new TempApp();
        var character = Load(temp, "dragonborn").Character;
        var dragonborn = Named(Origins, "Dragonborn");
        var sheet = temp.App.SaveCharacter(character with
        {
            Choices = [.. character.Choices.Where(c => c.Source != dragonborn), new(dragonborn, "draconic-ancestry", [Named(Origins, $"Draconic Ancestry: {dragon}")])],
        }).Sheet;

        var resistance = Assert.Single(sheet.DamageDefences!);
        Assert.Equal((damage, DamageDefenceKind.Resistance), (resistance.Type, resistance.Kind));
        // Breath Weapon: once per short or long rest. Its damage dice grow with character level (2d6 to 5d6), which
        // species content cannot scale on main, so the dice stay in the text.
        Assert.Equal(1, sheet.Resources!.Single(r => r.Label == "Breath Weapon").Maximum);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(3, true, false)]
    [InlineData(5, true, true)]
    public void Tiefling_infernal_legacy_uses_arrive_at_3rd_and_5th_level(int level, bool rebuke, bool darkness)
    {
        using var temp = new TempApp();
        var character = Load(temp, "tiefling").Character;
        var barbarian = character.Classes[0].Class;
        List<ChoiceSelection> choices = [.. character.Choices];
        if (level >= 3)
            choices.Add(new(barbarian, "primal-path", [new(Guid.Parse("51c00000-0000-4000-8000-000000000014"), Guid.Parse("51e00000-0000-4000-8000-000000000014"))]));
        var sheet = temp.App.SaveCharacter(character with { Level = level, Classes = [new(barbarian, level)], Choices = choices }).Sheet;

        Assert.Equal(rebuke, sheet.Resources!.Any(r => r.Label == "Hellish Rebuke (Infernal Legacy)" && r.Maximum == 1));
        Assert.Equal(darkness, sheet.Resources!.Any(r => r.Label == "Darkness (Infernal Legacy)" && r.Maximum == 1));
        Assert.Equal("fire", Assert.Single(sheet.DamageDefences!).Type);
    }

    [Fact]
    public void Every_revision_is_published_at_the_lowest_schema_version_it_needs()
    {
        var base51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1.json");
        var catalog = new InMemoryContentCatalog([.. base51.Sources], [.. base51.Revisions, .. Origins.Revisions]);

        foreach (var revision in Origins.Revisions)
        {
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.CanPublish, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
            Assert.True(revision.SchemaVersion == report.RequiredSchemaVersion, $"{revision.Name}: v{revision.SchemaVersion}, needs v{report.RequiredSchemaVersion}");
        }
    }

    [Fact]
    public void Every_published_revision_keeps_its_pinned_hash()
    {
        // Published revisions are insert-only: an in-place edit after release makes every launch log content.bundled-conflict.
        // tests/RulesFixtures/bundled/srd-5.1-origins.hashes.json pins each revision's stored hash (SqliteStore's own). Add a
        // line for a new revision; never change an existing one. TOMESTACK_WRITE_FIXTURES=1 rewrites the file (new packs only).
        var actual = Origins.Revisions.ToDictionary(r => r.RevisionId.ToString(), r => SqliteStore.Sha256(SqliteStore.Serialize(r)));
        if (Environment.GetEnvironmentVariable("TOMESTACK_WRITE_FIXTURES") == "1")
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repo.FullName, "TomeStack.slnx")))
                repo = repo.Parent!;
            var json = System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, string>(actual), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(repo.FullName, "tests", "RulesFixtures", "bundled", "srd-5.1-origins.hashes.json"), json.ReplaceLineEndings("\n") + "\n");
        }
        var pinned = TempApp.LoadFixture<Dictionary<string, string>>("bundled/srd-5.1-origins.hashes.json");

        Assert.All(pinned, p => Assert.Equal(p.Value, actual.GetValueOrDefault(p.Key)));
        Assert.Empty(actual.Keys.Except(pinned.Keys));
    }

    [Fact]
    public void The_new_ids_collide_with_no_other_bundled_content()
    {
        // Review focus 4: new content ids, never a changed published revision.
        var others = TomeStackApp.BundledPacks.Where(p => p != "TomeStack.Content.srd-5.1-origins.json").SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).ToList();

        Assert.Empty(Origins.Revisions.Select(r => r.ContentId).Intersect(others.Select(r => r.ContentId)));
        Assert.Empty(Origins.Revisions.Select(r => r.RevisionId).Intersect(others.Select(r => r.RevisionId)));
        Assert.Equal(Origins.Revisions.Count, Origins.Revisions.Select(r => r.ContentId).Distinct().Count());
        Assert.Equal(Origins.Revisions.Count, Origins.Revisions.Select(r => r.Name).Distinct().Count());
    }
}
