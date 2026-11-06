using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G9), Task 4: a <c>rollFloor</c> effect on the calculated sheet: which check fields it reaches, the highest
/// face and total winning separately, the trace step, and that a field's value never changes. Original fixtures on the companions pack.
/// </summary>
public class RollFloorV12SheetTests
{
    private static readonly Guid SourceId = Guid.Parse("6a7e0000-0000-4000-8000-000000000001");

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var pack = Fixtures.CompanionsPack();
        return new(pack.Sources, [.. pack.Revisions, .. extra]);
    }

    private static Character Handler() => Fixtures.Load("pm-handler-l3.json");

    private static ContentRevision Feature(string name, int n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e0097-0000-4000-8000-{0x200 + n:X12}"), RevisionId = Guid.Parse($"6a7e0097-0000-4000-8000-{0x300 + n:X12}"),
        SchemaVersion = 12, Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(SourceId), Status = RevisionStatus.Published, Effects = effects,
    };

    private static RollFloorEffect Floor(string id, string target, int? face = null, string? total = null, bool? proficient = null) =>
        new() { Id = id, Target = target, Face = face, Total = total, Proficient = proficient };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) =>
        CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(e => e.Reference)] }, Catalog(extra));

    private static IEnumerable<DerivedValue> Reached(CharacterSheet sheet) => sheet.Fields.Where(f => f.Floor is not null);

    [Fact]
    public void Face_floor_reaches_every_check_field_and_no_save()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Floor A", 1, Floor("f", "check", face: 8)));

        var ids = Reached(sheet).Select(f => f.Field).ToHashSet();
        foreach (var ability in Enum.GetValues<Ability>())
            Assert.Contains(FieldIds.Modifier(ability), ids);
        Assert.Contains(FieldIds.Initiative, ids);
        Assert.Contains(FieldIds.Skill("stealth"), ids);
        Assert.Contains(FieldIds.Skill("arcana"), ids);
        Assert.All(ids, id => Assert.True(
            id.StartsWith("skill.", StringComparison.Ordinal) || id.EndsWith(".mod", StringComparison.Ordinal) || id == FieldIds.Initiative, id));
        Assert.DoesNotContain(sheet.Fields.Where(f => f.Field.StartsWith("save.", StringComparison.Ordinal)), f => f.Floor is not null);
        Assert.Equal(8, sheet.Field(FieldIds.Skill("stealth")).Floor!.Face);
        Assert.Null(sheet.Field(FieldIds.Skill("stealth")).Floor!.Total);
    }

    [Fact]
    public void Ability_check_floor_reaches_mod_skills_and_initiative_for_dex()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Floor B", 2, Floor("f", "ability.dex.check", face: 10)));

        var ids = Reached(sheet).Select(f => f.Field).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "ability.dex.mod", FieldIds.Initiative, FieldIds.Skill("acrobatics"), FieldIds.Skill("sleightOfHand"), FieldIds.Skill("stealth") },
            ids);
    }

    [Fact]
    public void Proficient_floor_skips_non_proficient_skill_bare_check_and_initiative()
    {
        var feature = Feature("Fixture Floor C", 3,
            Floor("f", "check", face: 9, proficient: true),
            new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") });

        var sheet = Sheet(Handler(), feature);

        Assert.NotNull(sheet.Field(FieldIds.Skill("stealth")).Floor);
        Assert.Null(sheet.Field("ability.dex.mod").Floor);
        Assert.Null(sheet.Field(FieldIds.Initiative).Floor);
        foreach (var field in sheet.Fields.Where(f => f.Field.StartsWith("skill.", StringComparison.Ordinal)))
            Assert.Equal(field.Mark != ProficiencyMark.None, field.Floor is not null);
    }

    [Fact]
    public void Proficient_floor_reaches_expertise_skill()
    {
        var feature = Feature("Fixture Floor D", 4,
            Floor("f", "check", face: 9, proficient: true),
            new GrantEffect { Id = "g", Grant = GrantKind.Expertise, Target = FieldIds.Skill("stealth") });

        var sheet = Sheet(Handler(), feature);

        Assert.Equal(ProficiencyMark.Expertise, sheet.Field(FieldIds.Skill("stealth")).Mark);
        Assert.Equal(9, sheet.Field(FieldIds.Skill("stealth")).Floor!.Face);
    }

    [Fact]
    public void Total_floor_evaluates_STR_SCORE()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Floor E", 5, Floor("f", "skill.athletics", total: "STR.SCORE")));

        var strength = sheet.Field(FieldIds.Score(Ability.Str)).Value;
        var floor = sheet.Field(FieldIds.Skill("athletics")).Floor!;
        Assert.Equal(strength, floor.Total);
        Assert.Null(floor.Face);
        Assert.Equal(strength, Assert.Single(floor.Reasons).Total);
    }

    [Fact]
    public void Two_floors_highest_face_and_highest_total_win_both_listed()
    {
        var one = Feature("Fixture Floor F", 6, Floor("a", "skill.stealth", face: 6), Floor("b", "skill.stealth", total: "12"));
        var two = Feature("Fixture Floor G", 7, Floor("c", "skill.stealth", face: 9), Floor("d", "skill.stealth", total: "7"));

        var floor = Sheet(Handler(), one, two).Field(FieldIds.Skill("stealth")).Floor!;

        Assert.Equal((9, 12), (floor.Face, floor.Total));
        Assert.Equal([("Fixture Floor F", 6, null), ("Fixture Floor F", null, 12), ("Fixture Floor G", 9, null), ("Fixture Floor G", null, 7)],
            floor.Reasons.Select(r => (r.Source, r.Face, r.Total)));
    }

    [Fact]
    public void Failing_total_formula_leaves_the_reason_out_with_invalid_formula()
    {
        var feature = Feature("Fixture Floor H", 8, Floor("bad", "skill.stealth", total: "1 +"), Floor("ok", "skill.stealth", face: 5));

        var sheet = Sheet(Handler(), feature);

        var floor = sheet.Field(FieldIds.Skill("stealth")).Floor!;
        Assert.Equal(5, floor.Face);
        Assert.Null(floor.Total);
        Assert.Equal("Fixture Floor H", Assert.Single(floor.Reasons).Source);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == feature.Reference && d.EffectId == "bad");
    }

    [Fact]
    public void Floor_adds_one_floor_trace_step_after_the_fields_own_steps()
    {
        var without = Sheet(Handler()).Field(FieldIds.Skill("stealth"));
        var feature = Feature("Fixture Floor I", 9, Floor("f", "skill.stealth", face: 8), Floor("t", "skill.stealth", total: "15"));

        var field = Sheet(Handler(), feature).Field(FieldIds.Skill("stealth"));

        Assert.Equal((without.Value, without.ComputedValue), (field.Value, field.ComputedValue));
        var steps = field.Trace.Where(t => t.Operation == "floor").ToList();
        Assert.Equal(2, steps.Count);
        Assert.Equal(field.Trace.Count - 1, steps[0].Order);
        Assert.Equal(Enumerable.Range(1, field.Trace.Count), field.Trace.Select(t => t.Order));
        Assert.All(field.Trace.Take(field.Trace.Count - 2), t => Assert.NotEqual("floor", t.Operation));
        Assert.Equal((8, field.Value, "Roll floor from Fixture Floor I: a d20 below 8 counts as 8"), (steps[0].Amount, steps[0].Result, steps[0].Description));
        Assert.Equal((15, field.Value, "Roll floor from Fixture Floor I: a total below 15 counts as 15"), (steps[1].Amount, steps[1].Result, steps[1].Description));
        Assert.Equal(("Fixture Floor I", "f"), (steps[0].Origin.ContentName, steps[0].Origin.EffectId));
        Assert.Equal(field.Trace.Count, steps[1].Order);
    }

    [Fact]
    public void Field_without_a_floor_has_null_Floor_and_v11_sheet_json_is_unchanged()
    {
        var sheet = Sheet(Handler());

        Assert.All(sheet.Fields, f => Assert.Null(f.Floor));
        Assert.DoesNotContain("\"floor\":", JsonSerializer.Serialize(sheet.Fields, RulesJson.Compact), StringComparison.OrdinalIgnoreCase);
    }
}
