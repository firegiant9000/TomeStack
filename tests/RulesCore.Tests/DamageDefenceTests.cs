using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Wave 3 item 21 (D19 amended): the sheet's defences list merges the character's own <c>damageDefences</c> (character v9)
/// and the damage grants of content v10, normalised, collapsed per type and kind, with every source named.
/// </summary>
public class DamageDefenceTests
{
    private static readonly Guid DefenceContent = Guid.Parse("5fb10d00-0000-4000-8000-0000000000d1");

    private static ContentRevision Revision(string family, int schema = 10, params Effect[] effects) => new()
    {
        ContentId = DefenceContent, RevisionId = Guid.Parse("5fb10e00-0000-4000-8000-0000000000d1"), SchemaVersion = schema, Kind = ContentKind.Feat,
        Name = "Testy Hide", RulesFamilies = [family], Status = RevisionStatus.Published,
        Provenance = new(family == RulesFamilies.Srd51 ? Fixtures.Source2014 : Fixtures.Source2024, new(3)),
        Effects = effects,
    };

    private static GrantEffect Grant(string id, GrantKind kind, string? type) => new() { Id = id, Grant = kind, DamageType = type };

    private static CharacterSheet Sheet(ContentRevision revision, params DamageDefence[] manual) =>
        CharacterCalculator.Calculate(ToggleAndCostTests.Hero(revision, null) with { DamageDefences = manual.Length == 0 ? null : manual }, ToggleAndCostTests.CatalogWith(revision));

    [Fact]
    public void Nothing_is_written_when_the_character_has_no_defences()
    {
        var sheet = Sheet(Revision(RulesFamilies.Srd521, 10, new GrantEffect { Id = "p", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") }));

        Assert.Null(sheet.DamageDefences);
        Assert.DoesNotContain("damageDefences", JsonSerializer.Serialize(sheet, RulesJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_merges_manual_defences_and_grants_normalises_the_type_and_names_every_source()
    {
        var revision = Revision(RulesFamilies.Srd521, 10,
            Grant("hide-slash", GrantKind.DamageResistance, "Slashing"), // stored as written, compared in lower case
            Grant("hide-fire", GrantKind.DamageVulnerability, "FIRE"),
            Grant("hide-poison", GrantKind.DamageImmunity, "poison"));
        var sheet = Sheet(revision, new DamageDefence("slashing", DamageDefenceKind.Resistance), new DamageDefence("cold", DamageDefenceKind.Resistance));

        var list = sheet.DamageDefences!;
        Assert.Equal(
            [("cold", DamageDefenceKind.Resistance), ("fire", DamageDefenceKind.Vulnerability), ("poison", DamageDefenceKind.Immunity), ("slashing", DamageDefenceKind.Resistance)],
            list.Select(d => (d.Type, d.Kind)));

        // Slashing resistance has two sources (by hand and the feat): one entry, not two.
        var slashing = list.Single(d => d.Type == "slashing");
        Assert.Equal(2, slashing.Sources.Count);
        Assert.Contains(slashing.Sources, s => s.Content is null && s.Name == "Entered by hand");
        Assert.Contains(slashing.Sources, s => s.Content == revision.Reference && s.Name == "Testy Hide" && s.EffectId == "hide-slash");
        Assert.Equal(["Entered by hand"], list.Single(d => d.Type == "cold").Sources.Select(s => s.Name));
    }

    [Fact]
    public void The_same_defence_kind_from_several_sources_is_one_entry()
    {
        var revision = Revision(RulesFamilies.Srd521, 10, Grant("a", GrantKind.DamageResistance, "fire"), Grant("b", GrantKind.DamageResistance, "Fire"));
        var entry = Assert.Single(Sheet(revision, new DamageDefence("fire", DamageDefenceKind.Resistance)).DamageDefences!);

        Assert.Equal(3, entry.Sources.Count);
    }

    [Fact]
    public void A_grant_gated_by_level_applies_only_from_that_level()
    {
        var revision = Revision(RulesFamilies.Srd521, 10, Grant("late", GrantKind.DamageResistance, "cold") with { Level = 40 });

        Assert.Null(Sheet(revision).DamageDefences);

        var reachable = Revision(RulesFamilies.Srd521, 10, Grant("early", GrantKind.DamageResistance, "cold") with { Level = 1 });
        Assert.Single(Sheet(reachable).DamageDefences!);
    }

    [Fact]
    public void A_grant_the_calculator_cannot_apply_by_itself_is_not_a_defence_and_is_listed_as_assisted()
    {
        var conditional = Grant("sometimes", GrantKind.DamageResistance, "cold") with { Timing = EffectTiming.OnRoll };
        var assisted = Grant("by-hand", GrantKind.DamageImmunity, "acid") with { Automation = AutomationStatus.Assisted };
        var revision = Revision(RulesFamilies.Srd521, 10, conditional, assisted);
        var sheet = Sheet(revision);

        Assert.Null(sheet.DamageDefences);
        var feature = sheet.Features!.Single(f => f.Content == revision.Reference);
        Assert.Equal(AutomationStatus.Assisted, feature.Effects.Single(e => e.Id == "sometimes").Automation);
        Assert.Equal(AutomationStatus.Assisted, feature.Effects.Single(e => e.Id == "by-hand").Automation);
    }

    [Fact]
    public void An_automatic_always_on_damage_grant_is_reported_as_automatic()
    {
        var revision = Revision(RulesFamilies.Srd521, 10, Grant("hide", GrantKind.DamageResistance, "cold"));
        var sheet = Sheet(revision);

        var mechanic = Assert.Single(MechanicsInventory.Of(sheet, new HashSet<Guid> { Fixtures.Source2024 }).Mechanics, m => m.EffectId == "hide");
        Assert.Equal(AutomationStatus.Automatic, mechanic.Automation);
        Assert.Single(sheet.DamageDefences!);
    }

    [Fact]
    public void A_v9_revision_that_carries_a_damage_grant_gives_no_defence()
    {
        // Stored by an older build: the kind is a value a v9 build cannot read, so the effect stays unknown and does nothing.
        var node = JsonNode.Parse(JsonSerializer.Serialize(Revision(RulesFamilies.Srd521, 9, Grant("hide", GrantKind.Proficiency, null)), RulesJson.Compact))!;
        node["effects"]!.AsArray()[0] = JsonNode.Parse("{\"type\":\"grant\",\"id\":\"hide\",\"grant\":\"damageResistance\",\"damageType\":\"cold\"}");
        var stored = JsonSerializer.Deserialize<ContentRevision>(node.ToJsonString(RulesJson.Compact), RulesJson.Compact)!;
        Assert.IsType<UnknownEffect>(stored.Effects[0]);

        Assert.Null(Sheet(stored).DamageDefences);
        Assert.Null(Sheet(stored, new DamageDefence("fire", DamageDefenceKind.Resistance)).DamageDefences!.Single(d => d.Type == "fire").Sources.Single().Content);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void The_defences_list_is_the_same_in_both_families(string family)
    {
        var revision = Revision(family, 10, Grant("hide", GrantKind.DamageResistance, "Slashing"));
        var list = Sheet(revision, new DamageDefence("fire", DamageDefenceKind.Immunity)).DamageDefences!;

        Assert.Equal([("fire", DamageDefenceKind.Immunity), ("slashing", DamageDefenceKind.Resistance)], list.Select(d => (d.Type, d.Kind)));
    }
}
