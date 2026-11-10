using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content v12 G11 (condition immunity), Task 4a: the sheet's immunity list. A grant counts only when the calculator can apply it
/// by itself (automatic, always on) and its level, onlyAs and toggle gates are met; several sources of a condition are one entry.
/// </summary>
public class ConditionImmunityV12SheetTests
{
    private static readonly Guid Holder = Guid.Parse("5fb12000-0000-4000-8000-0000000000a1");
    private static readonly Guid Ward = Guid.Parse("5fb12000-0000-4000-8000-0000000000b2");

    private static ContentRevision Revision(Guid contentId, string name, params Effect[] effects) => new()
    {
        ContentId = contentId, RevisionId = Guid.NewGuid(), SchemaVersion = 12, Kind = ContentKind.Feat,
        Name = name, RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published,
        Provenance = new(Fixtures.Source2024, new(3)),
        Effects = effects,
    };

    private static GrantEffect Immune(string id, string condition) => new() { Id = id, Grant = GrantKind.ConditionImmunity, Condition = condition };

    private static CharacterSheet Sheet(PlayState? play, params ContentRevision[] revisions) =>
        CharacterCalculator.Calculate(
            ToggleAndCostTests.Hero(revisions[0], null, play: play) with { Pins = [.. revisions.Select(r => r.Reference)] },
            ToggleAndCostTests.CatalogWith(revisions));

    private static CharacterSheet Sheet(params ContentRevision[] revisions) => Sheet(null, revisions);

    [Fact]
    public void Always_on_immunity_is_listed_with_its_source()
    {
        var revision = Revision(Holder, "Unshaken Mind", Immune("calm", "charmed"));
        var entry = Assert.Single(Sheet(revision).ConditionImmunities!);

        Assert.Equal("charmed", entry.Condition);
        var source = Assert.Single(entry.Sources);
        Assert.Equal(new DamageDefenceSource("Unshaken Mind", revision.Reference, "calm"), source);
    }

    [Fact]
    public void Two_sources_of_one_condition_are_one_entry_in_key_order()
    {
        var first = Revision(Holder, "Unshaken Mind", Immune("a", "stunned"), Immune("b", "charmed"));
        var second = Revision(Ward, "Iron Will", Immune("c", "charmed"));
        var list = Sheet(first, second).ConditionImmunities!;

        Assert.Equal(["charmed", "stunned"], list.Select(v => v.Condition));
        Assert.Equal(["Unshaken Mind", "Iron Will"], list[0].Sources.Select(s => s.Name));
        Assert.Single(list[1].Sources);
    }

    [Fact]
    public void Toggled_immunity_is_listed_only_while_the_toggle_is_on()
    {
        var holder = Revision(Holder, "Fury Stance", new ToggleEffect { Id = "t", ToggleId = "fury", Label = "Fury" });
        var ward = Revision(Ward, "Furious Calm", Immune("calm", "frightened") with { WhileToggle = new(Holder, "fury") });

        Assert.Null(Sheet(holder, ward).ConditionImmunities);
        var on = Sheet(new PlayState { Toggles = [new ActiveToggle(Holder, "fury")] }, holder, ward);
        Assert.Equal("frightened", Assert.Single(on.ConditionImmunities!).Condition);
    }

    [Fact]
    public void Missing_toggle_content_reports_once_and_grants_nothing()
    {
        var ward = Revision(Ward, "Furious Calm", Immune("calm", "frightened") with { WhileToggle = new(Holder, "fury") });
        var sheet = Sheet(ward);

        Assert.Null(sheet.ConditionImmunities);
        Assert.Single(sheet.Diagnostics, d => d.Code == "modifier.toggle-content-missing" && d.EffectId == "calm");
    }

    [Fact]
    public void Manual_or_timed_immunity_is_not_listed_and_the_feature_is_assisted()
    {
        var timed = Immune("timed", "charmed") with { Timing = EffectTiming.OnRoll };
        var manual = Immune("by-hand", "poisoned") with { Automation = AutomationStatus.Assisted };
        var revision = Revision(Holder, "Sometimes Calm", timed, manual);
        var sheet = Sheet(revision);

        Assert.Null(sheet.ConditionImmunities);
        var feature = sheet.Features!.Single(f => f.Content == revision.Reference);
        Assert.Equal(AutomationStatus.Assisted, feature.Effects.Single(e => e.Id == "timed").Automation);
        Assert.Equal(AutomationStatus.Assisted, feature.Effects.Single(e => e.Id == "by-hand").Automation);
    }

    [Fact]
    public void Level_gate_applies()
    {
        Assert.Null(Sheet(Revision(Holder, "Late Calm", Immune("late", "charmed") with { Level = 40 })).ConditionImmunities);
        Assert.Single(Sheet(Revision(Holder, "Early Calm", Immune("early", "charmed") with { Level = 1 })).ConditionImmunities!);
    }

    [Fact]
    public void Sheet_without_condition_immunity_is_unchanged()
    {
        var sheet = Sheet(Revision(Holder, "Plain Feat", new GrantEffect { Id = "p", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") }));

        Assert.Null(sheet.ConditionImmunities);
        Assert.DoesNotContain("conditionImmunities", JsonSerializer.Serialize(sheet, RulesJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_immunity_grants_no_proficiency()
    {
        var with = Sheet(Revision(Holder, "Unshaken Mind", Immune("calm", "charmed")));
        var without = Sheet(Revision(Holder, "Unshaken Mind"));

        Assert.Equal(
            JsonSerializer.Serialize(without.Fields, RulesJson.Options),
            JsonSerializer.Serialize(with.Fields, RulesJson.Options));
    }
}
