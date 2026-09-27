using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// ROADMAP M1 exit gate: "Two rules-family fixture characters calculate and explain outputs". Two level-3 characters,
/// one per rules family, built only from the bundled SRD slice, each with a class, every choice resolved, and a
/// proficient save and skill. Every major number explains itself: each trace step names its origin, content steps
/// cite source and page, rules steps name the family, overrides keep the calculated value, and each field's trace
/// ends at its value. Expected values: docs/features/m1-acceptance.md.
/// </summary>
public class M1AcceptanceTests
{
    public static TheoryData<string> Characters() => ["m1-acceptance-srd51-korga.json", "m1-acceptance-srd521-brenna.json"];

    private static readonly string[] MajorFields =
    [
        .. Enum.GetValues<Ability>().SelectMany(a => new[] { FieldIds.Score(a), FieldIds.Modifier(a), FieldIds.Save(a) }),
        FieldIds.ProficiencyBonus, FieldIds.Initiative, FieldIds.ArmorClass, FieldIds.HitPoints,
        .. CharacterCalculator.Skills.Select(s => FieldIds.Skill(s.Key)),
    ];

    private static CharacterView Load(TempApp temp, string file) =>
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>($"characters/{file}"));

    [Theory]
    [MemberData(nameof(Characters))]
    public void Each_character_is_a_level_3_SRD_build_with_a_class_and_every_choice_resolved(string file)
    {
        using var temp = new TempApp();
        var view = Load(temp, file);
        var character = view.Character;
        var sheet = view.Sheet;
        var srdSources = temp.App.Store.ListSources().Where(s => s.License == "CC-BY-4.0").Select(s => s.Id).ToHashSet();

        Assert.True(character.TotalLevel >= 3);
        Assert.NotEmpty(character.Classes);
        Assert.Empty(sheet.Diagnostics); // nothing missing, refused, unresolved or disabled
        Assert.NotEmpty(sheet.Choices!);
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{c.SourceName} '{c.ChoiceId}'"));
        // Built from the SRD slice only: every active revision comes from an SRD source.
        Assert.All(sheet.Active!, r => Assert.Contains(temp.App.Store.FindRevision(r)!.Provenance.SourceId, srdSources));
        // A proficient save and a proficient skill.
        Assert.Contains(sheet.Fields, f => f.Field.StartsWith("save.", StringComparison.Ordinal) && IsProficient(f));
        Assert.Contains(sheet.Fields, f => f.Field.StartsWith("skill.", StringComparison.Ordinal) && IsProficient(f));
    }

    private static bool IsProficient(DerivedValue field) =>
        field.Trace.Any(t => t.Field == field.Field && t.Description.StartsWith("Proficiency bonus from", StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(Characters))]
    public void Every_major_number_explains_itself(string file)
    {
        using var temp = new TempApp();
        var sheet = Load(temp, file).Sheet;

        foreach (var id in MajorFields)
        {
            var field = sheet.Field(id);
            Assert.Equal(AutomationStatus.Automatic, field.Automation);
            Assert.NotEmpty(field.Trace);
            var own = field.Trace.Where(t => t.Field == id).ToList();
            Assert.True(own.Count > 0, $"{id} has no step of its own");
            Assert.Equal(field.Value, own[^1].Result); // the trace ends at the displayed value
            foreach (var step in field.Trace)
            {
                Assert.False(string.IsNullOrWhiteSpace(step.Description), $"{id}: a step has no description");
                Assert.Equal(sheet.RulesFamily, step.Origin.RulesFamily);
                if (step.Origin.Kind == TraceOriginKind.Content)
                {
                    Assert.NotNull(step.Origin.Content);
                    Assert.StartsWith("System Reference Document 5.", step.Origin.SourceTitle, StringComparison.Ordinal);
                    Assert.NotNull(step.Origin.Page);
                }
            }
            if (field.Override is { } fieldOverride)
            {
                Assert.Equal("override", own[^1].Operation);
                Assert.Contains($"computed value {field.ComputedValue}", own[^1].Description, StringComparison.Ordinal);
                Assert.Equal(fieldOverride.Value, field.Value);
            }
        }
    }

    [Fact]
    public void Korga_SRD_5_1_calculates_the_expected_values_with_species_increases_and_a_rolled_hit_point_override()
    {
        using var temp = new TempApp();
        var sheet = Load(temp, "m1-acceptance-srd51-korga.json").Sheet;
        int V(string field) => sheet.Field(field).Value;

        Assert.Equal((17, 15, 3, 2), (V(FieldIds.Score(Ability.Str)), V(FieldIds.Score(Ability.Con)), V(FieldIds.Modifier(Ability.Str)), V(FieldIds.ProficiencyBonus)));
        Assert.Equal((5, 4, 1), (V(FieldIds.Save(Ability.Str)), V(FieldIds.Save(Ability.Con)), V(FieldIds.Save(Ability.Dex))));
        Assert.Equal((5, 3, 2, 3, 1, 1, 1), (V(FieldIds.Skill("athletics")), V(FieldIds.Skill("perception")), V(FieldIds.Skill("intimidation")),
            V(FieldIds.Skill("insight")), V(FieldIds.Skill("religion")), V(FieldIds.Skill("survival")), V(FieldIds.Skill("animalHandling"))));
        Assert.Equal((1, 13), (V(FieldIds.Initiative), V(FieldIds.ArmorClass)));
        Assert.Equal((33, 32), (V(FieldIds.HitPoints), sheet.Field(FieldIds.HitPoints).ComputedValue));

        // Species ability increases apply under 2014 rules: Half-Orc, SRD 5.1 p. 7.
        var str = sheet.Field(FieldIds.Score(Ability.Str)).Trace.Single(t => t.Operation == "add");
        Assert.Equal(("Half-Orc", new PageRef(7), "System Reference Document 5.1"), (str.Origin.ContentName!, str.Origin.Page!, str.Origin.SourceTitle!));
        // Proficiencies say where they come from: a class choice, the species, the background.
        Assert.Contains("chosen from class 'Barbarian'", sheet.Field(FieldIds.Skill("athletics")).Trace[^1].Description, StringComparison.Ordinal);
        Assert.Contains("species 'Half-Orc'", sheet.Field(FieldIds.Skill("intimidation")).Trace[^1].Description, StringComparison.Ordinal);
        Assert.Contains("background 'Acolyte'", sheet.Field(FieldIds.Skill("insight")).Trace[^1].Description, StringComparison.Ordinal);
        // Armor class: Unarmored Defense replaces 10 + Dex, granted by the class at level 1 (p. 8).
        var ac = sheet.Field(FieldIds.ArmorClass).Trace.Single(t => t.Operation == "replace");
        Assert.Equal(("Unarmored Defense", new PageRef(8)), (ac.Origin.ContentName!, ac.Origin.Page!));
        Assert.Equal([new TraceInput("DEX.MOD", 1), new TraceInput("CON.MOD", 2)], ac.Inputs);
        // Hit points: d12 maximum, then 2 x 7, then Con +2 x 3 = 32; the rolled override (33) keeps 32 visible.
        var hp = sheet.Field(FieldIds.HitPoints).Trace.Where(t => t.Field == FieldIds.HitPoints).Select(t => t.Description).ToList();
        Assert.Equal(["Barbarian level 1: the hit die maximum (d12)", "Barbarian levels 2–3: 2 × 7 (fixed value for d12)", "Constitution modifier (+2) × 3 character level(s)",
            "User override (computed value 32): Rolled 8 and 7 at levels 2 and 3"], hp);
        // The level 3 subclass feature is active (chosen path, then granted at class level 3).
        Assert.Contains(sheet.Active!, r => temp.App.Store.FindRevision(r)!.Name == "Frenzy");
    }

    [Fact]
    public void Brenna_SRD_5_2_1_calculates_the_expected_values_with_background_increases_an_origin_feat_and_species_hit_points()
    {
        using var temp = new TempApp();
        var sheet = Load(temp, "m1-acceptance-srd521-brenna.json").Sheet;
        int V(string field) => sheet.Field(field).Value;

        Assert.Equal((17, 15, 3, 2), (V(FieldIds.Score(Ability.Str)), V(FieldIds.Score(Ability.Con)), V(FieldIds.Modifier(Ability.Str)), V(FieldIds.ProficiencyBonus)));
        Assert.Equal((5, 4, 1), (V(FieldIds.Save(Ability.Str)), V(FieldIds.Save(Ability.Con)), V(FieldIds.Save(Ability.Dex))));
        Assert.Equal((5, 3, 2, 1, -1, 3, 3), (V(FieldIds.Skill("athletics")), V(FieldIds.Skill("perception")), V(FieldIds.Skill("intimidation")),
            V(FieldIds.Skill("insight")), V(FieldIds.Skill("religion")), V(FieldIds.Skill("survival")), V(FieldIds.Skill("animalHandling"))));
        Assert.Equal((1, 13, 35), (V(FieldIds.Initiative), V(FieldIds.ArmorClass), V(FieldIds.HitPoints)));

        // Background ability increases apply under 2024 rules: the chosen Soldier option, SRD 5.2.1 p. 83.
        var str = sheet.Field(FieldIds.Score(Ability.Str)).Trace.Single(t => t.Operation == "add");
        Assert.Contains("chosen from background 'Soldier'", str.Description, StringComparison.Ordinal);
        Assert.Equal((new PageRef(83), "System Reference Document 5.2.1"), (str.Origin.Page!, str.Origin.SourceTitle!));
        // The 2024 background grants its origin feat.
        Assert.Contains(sheet.Active!, r => temp.App.Store.FindRevision(r)!.Name == "Savage Attacker");
        // Primal Knowledge (class level 3) adds a chosen skill: Animal Handling.
        Assert.Contains("chosen from feature 'Primal Knowledge'", sheet.Field(FieldIds.Skill("animalHandling")).Trace[^1].Description, StringComparison.Ordinal);
        // Hit points: 12 + 2 x 7 + Con +2 x 3 = 32, plus Dwarven Toughness (LEVEL = 3, species, p. 84).
        var toughness = sheet.Field(FieldIds.HitPoints).Trace[^1];
        Assert.Equal(("Dwarf", new PageRef(84), 3), (toughness.Origin.ContentName!, toughness.Origin.Page!, toughness.Amount!.Value));
        Assert.Equal([new TraceInput(FormulaIdentifiers.Level, 3)], toughness.Inputs);
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void Each_character_round_trips_through_a_package_to_a_clean_data_folder(string file)
    {
        using var origin = new TempApp();
        var view = Load(origin, file);

        using var destination = new TempApp();
        destination.App.ApplyImport(origin.App.ExportCharacters([view.Character.Id], Packages.ExportPurpose.Share).Content);

        Assert.Equal(TempApp.Json(view.Sheet), TempApp.Json(destination.App.GetCharacter(view.Character.Id).Sheet));
    }
}
