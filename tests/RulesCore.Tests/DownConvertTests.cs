using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

public class DownConvertTests
{
    private static readonly Guid ContentId = Guid.Parse("6f0dc000-0000-4000-8000-0000000000b1");

    private static Character Base() => Fixtures.Srd521Character() with { Name = "Compat Hero" };

    private static readonly CustomItem Rope = new(Guid.Parse("6f0dc000-0000-4000-8000-0000000000b2"), "Rope", 1, null, false);

    private static Character V9() => Base() with { Xp = 300 };

    [Fact]
    public void Character_below_9_blocks_a_running_encounter()
    {
        var input = Base() with { Play = Base().Play with { Encounter = new EncounterState(3) } };

        var result = DownConvert.Below(input, 9);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new BelowChange("play.encounter", BelowAction.Blocks, "below.encounter-running", "'Compat Hero': an encounter is running. End it, then export."), change);
        Assert.True(result.Blocked);
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_9_drops_rounds_left_and_writes_v9()
    {
        var input = V9() with { Play = Base().Play with { Toggles = [new ActiveToggle(ContentId, "tideStance", RoundsLeft: 2)] } };
        Assert.Equal(10, input.SchemaVersion);

        var result = DownConvert.Below(input, 9);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new BelowChange("play.toggles[0].roundsLeft", BelowAction.Dropped, "below.rounds-left", "'Compat Hero': the rounds left on 'tideStance'."), change);
        Assert.False(result.Blocked);
        Assert.Equal(9, result.Character.SchemaVersion);
        var toggle = Assert.Single(result.Character.Play.Toggles);
        Assert.Null(toggle.RoundsLeft);
        Assert.Equal("tideStance", toggle.ToggleId);
    }

    public static TheoryData<string, string, string, Func<Character, Character>> V9Fields => new()
    {
        { "details", "details", "below.details", c => c with { Details = new CharacterDetails(Backstory: "Raised on the tide.") } },
        { "customItems", "customItems", "below.custom-items", c => c with { CustomItems = [Rope] } },
        { "customAttacks", "customAttacks", "below.custom-attacks", c => c with { CustomAttacks = [new CustomAttack(Rope.Id, "Cudgel", 3, "1d6", "bludgeoning", null)] } },
        { "damageDefences", "damageDefences", "below.damage-defences", c => c with { DamageDefences = [new DamageDefence("fire", DamageDefenceKind.Resistance)] } },
        { "companions", "companions", "below.companions", c => c with { Companions = [new Companion(Rope.Id, "Gull", null, null, 4, null, null, null)] } },
        { "play.companions", "play.companions", "below.companions", c => c with { Play = c.Play with { Companions = [new CompanionPlay(Rope.Id, 2, 0)] } } },
        { "xp", "xp", "below.xp", c => c with { Xp = 300 } },
        { "equipment attuned", "equipment[0].attuned", "below.equipment-state", c => c with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Attuned: true)] } },
        { "equipment chargesSpent", "equipment[0].chargesSpent", "below.equipment-state", c => c with { Equipment = [new EquipmentEntry(Fixtures.Bracers, ChargesSpent: 2)] } },
    };

    [Theory]
    [MemberData(nameof(V9Fields))]
    public void Character_below_8_drops_each_v9_field(string label, string path, string code, Func<Character, Character> set)
    {
        var input = set(Base());
        Assert.Equal(9, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal(path, change.Path);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal(code, change.Code);
        Assert.StartsWith("'Compat Hero': ", change.Message);
        Assert.False(result.Blocked);
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.NotEmpty(label);
    }

    [Fact]
    public void Character_below_8_clears_the_equipment_id_when_attuned()
    {
        var input = Base() with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Attuned: true, Id: Rope.Id)] };

        var result = DownConvert.Below(input, 8);

        Assert.Equal("equipment[0].attuned", Assert.Single(result.Changes).Path);
        Assert.Null(result.Character.Equipment[0].Id);
        Assert.Null(result.Character.Equipment[0].Attuned);
    }

    [Fact]
    public void Character_below_8_silently_drops_an_equipment_id_alone()
    {
        var input = Base() with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Id: Rope.Id)] };
        Assert.Equal(9, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal("equipment[0].id", change.Path);
        Assert.Equal(BelowAction.Silent, change.Action);
        Assert.Equal("below.equipment-state", change.Code);
        Assert.False(result.Blocked);
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.Null(result.Character.Equipment[0].Id);
    }

    [Fact]
    public void Character_below_8_blocks_a_vulnerability_entry()
    {
        var input = Base() with { DamageDefences = [new DamageDefence("fire", DamageDefenceKind.Vulnerability)] };

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal("damageDefences[0]", change.Path);
        Assert.Equal(BelowAction.Blocks, change.Action);
        Assert.Equal("below.vulnerability", change.Code);
        Assert.True(result.Blocked);
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_8_lists_a_vulnerability_with_the_other_drops_and_returns_the_input()
    {
        var input = Base() with
        {
            Xp = 10,
            DamageDefences = [new DamageDefence("cold", DamageDefenceKind.Resistance), new DamageDefence("fire", DamageDefenceKind.Vulnerability)],
        };

        var result = DownConvert.Below(input, 8);

        Assert.Contains(result.Changes, c => c.Path == "xp" && c.Action == BelowAction.Dropped);
        Assert.Contains(result.Changes, c => c.Path == "damageDefences[1]" && c.Action == BelowAction.Blocks);
        Assert.DoesNotContain(result.Changes, c => c.Code == "below.damage-defences");
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_returns_same_instance_when_nothing_is_above()
    {
        var v8 = Base();
        Assert.Equal(8, v8.SchemaVersion);
        var low = DownConvert.Below(v8, 8);
        Assert.Same(v8, low.Character);
        Assert.Empty(low.Changes);

        var v10 = Base() with { Play = Base().Play with { Encounter = new EncounterState(1) } };
        var top = DownConvert.Below(v10, Character.CurrentSchemaVersion);
        Assert.Same(v10, top.Character);
        Assert.Empty(top.Changes);
    }

    [Fact]
    public void Character_below_rejects_out_of_range_target()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(Base(), 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(Base(), Character.CurrentSchemaVersion + 1));
        Assert.Throws<ArgumentNullException>(() => DownConvert.Below(null!, 8));
    }

    [Fact]
    public void Character_below_8_lists_both_steps_for_a_v10_character()
    {
        var input = Base() with
        {
            Xp = 300,
            Play = Base().Play with { Toggles = [new ActiveToggle(ContentId, "tideStance", RoundsLeft: 2)] },
        };
        Assert.Equal(10, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        Assert.Equal(["play.toggles[0].roundsLeft", "xp"], result.Changes.Select(c => c.Path).ToArray());
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.Null(result.Character.Xp);
        Assert.Null(Assert.Single(result.Character.Play.Toggles).RoundsLeft);
    }

    // ---- content: v11 -> v10 -------------------------------------------------------------------------------------------

    private static ContentRevision Rev(string effects, int schema = 11, string name = "Compat Steady Hands") =>
        Fixtures.RevisionWithEffect(effects, schema) with { Name = name };

    private static string Written(ContentBelow below) => JsonSerializer.Serialize(below.Revision, RulesJson.Compact);

    private const string Other = "6f0dc000-0000-4000-8000-0000000000b1";

    [Fact]
    public void Content_below_10_drops_attack_rider_response_value()
    {
        var input = Rev("""
            {"type":"attack","id":"a1","attackId":"a1","label":"Strike","kind":"melee","abilities":["str"]},
            {"type":"attackRider","id":"r1","riderId":"r1","label":"Rider","extraDamage":"1d4"},
            {"type":"response","id":"p1","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"1"},
            {"type":"value","id":"v1","valueId":"focusMod","formula":"WIS.MOD"},
            {"type":"modifier","id":"m1","operation":"bonus","target":"initiative","value":"1"}
            """);

        var result = DownConvert.Below(input, 10);

        Assert.Equal(["effects[0]", "effects[1]", "effects[2]", "effects[3]"], result.Changes.Select(c => c.Path).ToArray());
        Assert.All(result.Changes, c =>
        {
            Assert.Equal(BelowAction.Dropped, c.Action);
            Assert.Equal("below.effect-unsupported", c.Code);
            Assert.StartsWith("'Compat Steady Hands': ", c.Message);
        });
        Assert.Equal("'Compat Steady Hands': its attack; the feature stays as text.", result.Changes[0].Message);
        Assert.False(result.Blocked);
        Assert.Equal(10, result.Revision.SchemaVersion);
        Assert.Equal(["m1"], result.Revision.Effects.Select(e => e.Id).ToArray());
        var json = Written(result);
        foreach (var id in new[] { "a1", "r1", "p1", "v1" })
            Assert.DoesNotContain($"\"id\":\"{id}\"", json);
        Assert.Null(result.Revision.Extensions);
        Assert.All(result.Revision.Effects, e => Assert.Null(e.Extensions));
    }

    [Fact]
    public void Content_below_10_blocks_upkeep_that_spends()
    {
        var input = Rev("""{"type":"upkeep","id":"u","at":"turnEnd","resourceId":"r","change":"spend","amount":"1"}""");

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0]", BelowAction.Blocks, "below.upkeep"), (change.Path, change.Action, change.Code));
        Assert.True(result.Blocked);
        Assert.Empty(result.Revision.Effects);
        Assert.Equal(10, result.Revision.SchemaVersion);
    }

    [Fact]
    public void Content_below_10_drops_upkeep_that_only_regains()
    {
        var input = Rev("""{"type":"upkeep","id":"u","at":"turnEnd","resourceId":"r","change":"regain","amount":"all"}""");

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0]", BelowAction.Dropped, "below.upkeep"), (change.Path, change.Action, change.Code));
        Assert.False(result.Blocked);
        Assert.Empty(result.Revision.Effects);
    }

    public static TheoryData<string, string, string, string> BlockingKeys => new()
    {
        { """{"type":"toggleGroup","id":"g","groupId":"forms","label":"Forms"}""", "effects[0]", "below.toggle-group", "" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms"}""", "effects[0].group", "below.toggle-group", "group" },
        { $$"""{"type":"toggle","id":"t","toggleId":"t","label":"T","groupContent":"{{Other}}"}""", "effects[0].groupContent", "below.toggle-group", "groupContent" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","allowedWith":[{"toggleId":"u"}]}""", "effects[0].allowedWith", "below.toggle-group", "allowedWith" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","allowedInBase":true}""", "effects[0].allowedInBase", "below.toggle-group", "allowedInBase" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","layer":"overlay"}""", "effects[0].layer", "below.toggle-group", "layer" },
        { """{"type":"resource","id":"pool","resourceId":"pool","label":"Pool","maximum":"2"},{"type":"toggle","id":"t","toggleId":"t","label":"T","cost":"1","resourceId":"pool"}""", "effects[1].cost", "below.toggle-cost", "cost" },
        { $$"""{"type":"toggle","id":"t","toggleId":"t","label":"T","resourceContent":"{{Other}}"}""", "effects[0].resourceContent", "below.toggle-cost", "resourceContent" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","level":3}""", "effects[0].level", "below.toggle-level", "level" },
        { $$"""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1","toggle":"t","toggleContent":"{{Other}}"}""", "effects[0].toggleContent", "below.modifier-toggle-content", "toggleContent" },
        { """{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"toggleId":"sky"}}""", "effects[0].whileToggle", "below.defence-while-toggle", "whileToggle" },
    };

    [Theory]
    [MemberData(nameof(BlockingKeys))]
    public void Content_below_10_blocks_toggle_group_cost_level_and_modifier_toggle_content(string effects, string path, string code, string key)
    {
        var input = Rev(effects);

        var result = DownConvert.Below(input, 10);

        // Some rows need a companion key (a group) to be valid v11, so look the change up by path.
        var change = Assert.Single(result.Changes, c => c.Path == path);
        Assert.Equal((BelowAction.Blocks, code), (change.Action, change.Code));
        Assert.True(result.Blocked);
        Assert.Equal(10, result.Revision.SchemaVersion);
        if (key.Length > 0)
        {
            Assert.DoesNotContain($"\"{key}\":", Written(result));
            Assert.Equal(input.Effects.Count, result.Revision.Effects.Count);
        }
        else
            Assert.DoesNotContain("toggleGroup", Written(result));
    }

    public static TheoryData<string, string, string> DroppedKeys => new()
    {
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3"}""", "duration", "below.toggle-duration" },
        { """{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"2","allowance":true}""", "allowance", "below.resource-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","whileToggle":{"toggleId":"t"}}""", "whileToggle", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","save":{"ability":"dex","dc":"8"}}""", "save", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","area":{"shape":"cone","size":15}}""", "area", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","range":"30 feet"}""", "range", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","targets":"one creature"}""", "targets", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","cost":"1","resourceId":"p","perUnit":{"dice":"1d6"}}""", "perUnit", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","drawsDown":true}""", "drawsDown", "below.roll-detail" },
    };

    [Theory]
    [MemberData(nameof(DroppedKeys))]
    public void Content_below_10_drops_toggle_duration_and_roll_detail_fields(string effect, string key, string code)
    {
        var input = Rev(effect);
        Assert.IsNotType<UnknownEffect>(input.Effects.Single());

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(($"effects[0].{key}", BelowAction.Dropped, code), (change.Path, change.Action, change.Code));
        Assert.False(result.Blocked);
        var remaining = Assert.Single(result.Revision.Effects);
        Assert.IsNotType<UnknownEffect>(remaining);
        Assert.DoesNotContain($"\"{key}\":", Written(result));
        Assert.Null(remaining.Extensions);
    }

    [Fact]
    public void Content_below_10_drops_resource_full_next_to_counts_up()
    {
        var result = DownConvert.Below(Rev("""{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"3","counts":"up","resourceFull":{"text":"Full"}}"""), 10);

        Assert.Equal(["effects[0].counts", "effects[0].resourceFull"], result.Changes.Select(c => c.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        var full = Assert.Single(result.Changes, c => c.Path.EndsWith("resourceFull", StringComparison.Ordinal));
        Assert.Equal((BelowAction.Dropped, "below.resource-detail"), (full.Action, full.Code));
        Assert.DoesNotContain("resourceFull", Written(result));
    }

    [Fact]
    public void Content_below_10_drops_a_false_draws_down_silently()
    {
        var result = DownConvert.Below(Rev("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","drawsDown":false}"""), 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0].drawsDown", BelowAction.Silent, "below.roll-detail"), (change.Path, change.Action, change.Code));
        Assert.IsType<RollEffect>(Assert.Single(result.Revision.Effects));
        Assert.DoesNotContain("drawsDown", Written(result));
    }

    [Fact]
    public void Content_below_10_blocks_counts_up_and_drops_counts_down()
    {
        var up = DownConvert.Below(Rev("""{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"3","counts":"up"}"""), 10);
        var upChange = Assert.Single(up.Changes);
        Assert.Equal(("effects[0].counts", BelowAction.Blocks, "below.resource-counts-up"), (upChange.Path, upChange.Action, upChange.Code));
        Assert.True(up.Blocked);
        Assert.DoesNotContain("\"counts\"", Written(up));

        var upper = DownConvert.Below(Rev("""{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"3","counts":"UP"}"""), 10);
        Assert.True(upper.Blocked);

        var down = DownConvert.Below(Rev("""{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"3","counts":"down"}"""), 10);
        var downChange = Assert.Single(down.Changes);
        Assert.Equal(("effects[0].counts", BelowAction.Dropped, "below.resource-detail"), (downChange.Path, downChange.Action, downChange.Code));
        Assert.False(down.Blocked);
        Assert.DoesNotContain("\"counts\"", Written(down));
        Assert.IsType<ResourceEffect>(Assert.Single(down.Revision.Effects));
    }

    [Fact]
    public void Content_below_10_drops_a_diceless_roll_as_a_whole()
    {
        var input = Rev("""{"type":"roll","id":"r","rollId":"r","label":"R","range":"30 feet","targets":"one creature"}""");

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0]", BelowAction.Dropped, "below.diceless-roll"), (change.Path, change.Action, change.Code));
        Assert.Empty(result.Revision.Effects);
    }

    [Fact]
    public void Content_below_10_roll_also_change_spend_blocks_regain_drops()
    {
        const string head = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":""";
        var regainEntry = $$"""{"resourceId":"charge","resourceContent":"{{Other}}","change":"regain","amount":"1"}""";
        var spendEntry = $$"""{"resourceId":"charge","resourceContent":"{{Other}}","change":"spend","amount":"1"}""";

        var regain = DownConvert.Below(Rev($"{head}[{regainEntry}]}}"), 10);
        var change = Assert.Single(regain.Changes);
        Assert.Equal(("effects[0].alsoChange", BelowAction.Dropped, "below.roll-also-change"), (change.Path, change.Action, change.Code));
        Assert.False(regain.Blocked);

        var mixed = DownConvert.Below(Rev($"{head}[{regainEntry},{spendEntry}]}}"), 10);
        var blocked = Assert.Single(mixed.Changes);
        Assert.Equal(("effects[0].alsoChange", BelowAction.Blocks, "below.roll-also-change"), (blocked.Path, blocked.Action, blocked.Code));
        Assert.True(mixed.Blocked);
        Assert.DoesNotContain("alsoChange", Written(mixed));
        Assert.IsType<RollEffect>(Assert.Single(mixed.Revision.Effects));
    }

    [Fact]
    public void Content_below_10_roll_mode_advantage_drops_disadvantage_blocks()
    {
        var advantage = DownConvert.Below(Rev("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage"}"""), 10);
        var drop = Assert.Single(advantage.Changes);
        Assert.Equal(("effects[0]", BelowAction.Dropped, "below.effect-unsupported"), (drop.Path, drop.Action, drop.Code));
        Assert.False(advantage.Blocked);
        Assert.Empty(advantage.Revision.Effects);

        var disadvantage = DownConvert.Below(Rev("""{"type":"rollMode","id":"m","target":"save.dex","mode":"disadvantage"}"""), 10);
        var block = Assert.Single(disadvantage.Changes);
        Assert.Equal(("effects[0]", BelowAction.Blocks, "below.roll-mode-disadvantage"), (block.Path, block.Action, block.Code));
        Assert.True(disadvantage.Blocked);
        Assert.Empty(disadvantage.Revision.Effects);
    }

    [Fact]
    public void Content_below_uses_the_original_effect_index_in_paths()
    {
        var input = Rev("""
            {"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"},
            {"type":"attack","id":"a","attackId":"a","label":"Strike","kind":"melee","abilities":["str"]},
            {"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3"}
            """);

        var result = DownConvert.Below(input, 10);

        Assert.Equal(["effects[1]", "effects[2].duration"], result.Changes.Select(c => c.Path).ToArray());
        Assert.Equal(["m", "t"], result.Revision.Effects.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Content_below_returns_same_instance_when_nothing_is_above()
    {
        var v9 = Rev("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""", 9);
        var low = DownConvert.Below(v9, 10);
        Assert.Same(v9, low.Revision);
        Assert.Empty(low.Changes);

        var v11 = Rev("""{"type":"attack","id":"a","attackId":"a","label":"Strike","kind":"melee","abilities":["str"]}""");
        var top = DownConvert.Below(v11, ContentRevision.CurrentSchemaVersion);
        Assert.Same(v11, top.Revision);
        Assert.Empty(top.Changes);
    }

    [Fact]
    public void Content_below_10_lowers_the_version_even_with_nothing_to_drop()
    {
        var input = Rev("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""");

        var result = DownConvert.Below(input, 10);

        Assert.Empty(result.Changes);
        Assert.Equal(10, result.Revision.SchemaVersion);
        Assert.Equal(11, input.SchemaVersion);
    }

    [Fact]
    public void Content_below_10_removes_inert_v11_keys_silently()
    {
        var input = Rev("""{"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3"}""", 10);
        var toggle = Assert.IsType<ToggleEffect>(Assert.Single(input.Effects));
        Assert.True(toggle.Extensions?.ContainsKey("duration"));

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0].duration", BelowAction.Silent, "below.toggle-duration"), (change.Path, change.Action, change.Code));
        Assert.False(result.Blocked);
        Assert.Equal(10, result.Revision.SchemaVersion);
        var lowered = Assert.IsType<ToggleEffect>(Assert.Single(result.Revision.Effects));
        Assert.Null(lowered.Extensions);
    }

    [Fact]
    public void Content_below_leaves_v11_type_effects_in_an_older_revision_alone()
    {
        var input = Rev("""{"type":"upkeep","id":"u","at":"turnEnd","resourceId":"r","change":"spend","amount":"1"}""", 10);
        Assert.IsType<UnknownEffect>(Assert.Single(input.Effects));

        var result = DownConvert.Below(input, 10);

        Assert.Empty(result.Changes);
        Assert.Same(input, result.Revision);
    }

    [Fact]
    public void Content_below_never_touches_ids_provenance_status()
    {
        var input = Rev("""{"type":"attack","id":"a","attackId":"a","label":"Strike","kind":"melee","abilities":["str"]}""");

        var result = DownConvert.Below(input, 9);

        var lowered = result.Revision;
        Assert.Equal(9, lowered.SchemaVersion);
        Assert.Equal((input.ContentId, input.RevisionId, input.Status, input.Name), (lowered.ContentId, lowered.RevisionId, lowered.Status, lowered.Name));
        Assert.Equal(input.Provenance, lowered.Provenance);
        Assert.Equal(input.Kind, lowered.Kind);
    }

    [Fact]
    public void Content_below_10_roll_also_change_null_is_silent_and_a_non_array_blocks()
    {
        const string head = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":""";

        // A v11 reader treats null as absent, so nothing is there to lower; a v10 revision keeps the null as extension data.
        Assert.Empty(DownConvert.Below(Rev(head + "null}"), 10).Changes);
        var empty = DownConvert.Below(Rev(head + "null}", 10), 10);
        var silent = Assert.Single(empty.Changes);
        Assert.Equal(("effects[0].alsoChange", BelowAction.Silent, "below.roll-also-change"), (silent.Path, silent.Action, silent.Code));
        Assert.False(empty.Blocked);
        Assert.DoesNotContain("alsoChange", Written(empty));

        var scalar = DownConvert.Below(Rev(head + "\"spend\"}"), 10);
        var blocked = Assert.Single(scalar.Changes);
        Assert.Equal(("effects[0].alsoChange", BelowAction.Blocks, "below.roll-also-change"), (blocked.Path, blocked.Action, blocked.Code));
        Assert.True(scalar.Blocked);
        Assert.DoesNotContain("alsoChange", Written(scalar));
    }

    [Theory]
    [InlineData("""{"type":"toggle","id":"t","toggleId":"t","label":"T","DURATION":"3"}""", "DURATION", "effects[0].duration", BelowAction.Dropped, "below.toggle-duration")]
    [InlineData("""{"type":"toggle","id":"t","toggleId":"t","label":"T","Group":"forms"}""", "Group", "effects[0].group", BelowAction.Blocks, "below.toggle-group")]
    public void Content_below_10_matches_keys_ignoring_case(string effect, string written, string path, BelowAction action, string code)
    {
        var result = DownConvert.Below(Rev(effect), 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal((path, action, code), (change.Path, change.Action, change.Code));
        Assert.DoesNotContain($"\"{written}\"", Written(result), StringComparison.OrdinalIgnoreCase);
        Assert.Null(Assert.IsType<ToggleEffect>(Assert.Single(result.Revision.Effects)).Extensions);
    }

    [Theory]
    [InlineData("null", 10)]
    [InlineData("false", 11)]
    public void Content_below_10_draws_down_null_or_false_is_silent(string value, int schema)
    {
        // A v11 reader drops a null on its own, so null is only visible as extension data of a v10 revision.
        var result = DownConvert.Below(Rev($$"""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","drawsDown":{{value}}}""", schema), 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("effects[0].drawsDown", BelowAction.Silent, "below.roll-detail"), (change.Path, change.Action, change.Code));
        Assert.DoesNotContain("drawsDown", Written(result));
    }

    // Deliberate: in a revision that declares v11, v11 keys are lowered by the table even when the effect failed to type
    // (here a roll whose perUnit is malformed stays an UnknownEffect), because the key is still in the file and a v10 reader would see it.
    [Fact]
    public void Content_below_10_lowers_v11_keys_on_an_untyped_effect_of_a_v11_revision()
    {
        var input = Rev("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","range":"30 ft","perUnit":"1d6"}""");
        Assert.IsType<UnknownEffect>(Assert.Single(input.Effects));

        var result = DownConvert.Below(input, 10);

        Assert.Equal(["effects[0].perUnit", "effects[0].range"], result.Changes.Select(c => c.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.All(result.Changes, c => Assert.Equal(BelowAction.Dropped, c.Action));
        Assert.DoesNotContain("perUnit", Written(result));
        Assert.DoesNotContain("range", Written(result));
    }

    public static TheoryData<string, string, string> InertKeys => new()
    {
        { """{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"2","allowance":true}""", "effects[0].allowance", "below.resource-detail" },
        { """{"type":"resource","id":"p","resourceId":"p","label":"P","maximum":"3","counts":"up"}""", "effects[0].counts", "below.resource-counts-up" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","range":"30 ft"}""", "effects[0].range", "below.roll-detail" },
        { """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"c","change":"spend","amount":"1"}]}""", "effects[0].alsoChange", "below.roll-also-change" },
        { """{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1","toggle":"t","toggleContent":"6f0dc000-0000-4000-8000-0000000000b1"}""", "effects[0].toggleContent", "below.modifier-toggle-content" },
        { """{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"toggleId":"sky"}}""", "effects[0].whileToggle", "below.defence-while-toggle" },
        { """{"type":"toggle","id":"t","toggleId":"t","label":"T","level":3}""", "effects[0].level", "below.toggle-level" },
    };

    [Theory]
    [MemberData(nameof(InertKeys))]
    public void Content_below_removes_inert_v11_keys_silently_on_every_effect_type(string effect, string path, string code)
    {
        var input = Rev(effect, 10);
        var typed = Assert.Single(input.Effects);
        Assert.IsNotType<UnknownEffect>(typed);
        Assert.NotNull(typed.Extensions);

        var result = DownConvert.Below(input, 10);

        var change = Assert.Single(result.Changes);
        Assert.Equal((path, BelowAction.Silent, code), (change.Path, change.Action, change.Code));
        Assert.False(result.Blocked);
        Assert.Null(Assert.Single(result.Revision.Effects).Extensions);
    }

    [Fact]
    public void Content_below_is_deterministic()
    {
        var input = Rev("""
            {"type":"attack","id":"a","attackId":"a","label":"Strike","kind":"melee","abilities":["str"]},
            {"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3","level":2},
            {"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}
            """);

        var first = DownConvert.Below(input, 9);
        var second = DownConvert.Below(input, 9);

        Assert.Equal(Written(first), Written(second));
        Assert.Equal(first.Changes, second.Changes);
    }

    [Fact]
    public void Content_below_keeps_grant_content_choice_options_and_extends_choice()
    {
        var grantJson = $$$"""{"type":"grant","id":"gc","grant":"content","content":{"contentId":"{{{Other}}}","revisionId":"{{{Other}}}"}}""";
        var choiceJson = $$$"""{"type":"choice","id":"c","choiceId":"pick","count":1,"options":[{"contentId":"{{{Other}}}","revisionId":"{{{Other}}}"}]}""";
        var toggleJson = """{"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3"}""";
        var input = Rev($"{grantJson},{choiceJson},{toggleJson}") with { ExtendsChoice = new ChoiceExtension(Guid.Parse(Other), "pick") };
        var before = input.Effects.Take(2).Select(e => JsonSerializer.Serialize(e, RulesJson.Compact)).ToArray();

        var result = DownConvert.Below(input, 10);

        Assert.Equal(input.ExtendsChoice, result.Revision.ExtendsChoice);
        Assert.Equal(before, result.Revision.Effects.Take(2).Select(e => JsonSerializer.Serialize(e, RulesJson.Compact)).ToArray());
        Assert.Contains(Other, before[0], StringComparison.Ordinal);
        Assert.Contains(Other, before[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Content_below_rejects_out_of_range_target()
    {
        var input = Rev("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""");
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(input, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(input, ContentRevision.CurrentSchemaVersion + 1));
        Assert.Throws<ArgumentNullException>(() => DownConvert.Below((ContentRevision)null!, 10));
    }
}
