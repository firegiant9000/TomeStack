using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Character schema v10 (D43 slice 3): the encounter round and the rounds left on a toggle. The version written is computed from
/// the data (R73), three tiers: v10 only when an encounter or a rounds-left count is set, otherwise v9 when a v9 field is set,
/// otherwise v8, so a character that never starts an encounter stays readable by the previous release.
/// </summary>
public class CharacterSchemaV10Tests
{
    private static Character V10() => Fixtures.Load("srd521-courier-v10.json");

    private static Character V9() => Fixtures.Load("srd521-courier-v9.json");

    private static Character V8() => Fixtures.Load("srd521-courier-v8.json");

    private static string Write(Character character) => JsonSerializer.Serialize(character, RulesJson.Compact);

    private static List<string> Codes(Character character) => [.. character.Validate().Select(d => d.Code)];

    [Fact]
    public void A_character_with_no_encounter_is_written_at_v9_or_v8()
    {
        Assert.Equal(8, V8().SchemaVersion);
        Assert.Contains("\"schemaVersion\":8", Write(V8()), StringComparison.Ordinal);
        Assert.Equal(9, V9().SchemaVersion);
        Assert.Contains("\"schemaVersion\":9", Write(V9()), StringComparison.Ordinal);
        Assert.DoesNotContain("encounter", Write(V9()), StringComparison.Ordinal);
        Assert.DoesNotContain("roundsLeft", Write(V9()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_in_an_encounter_is_written_at_v10()
    {
        var inEncounter = V9() with { Play = V9().Play with { Encounter = new(0) } };
        Assert.Equal(10, inEncounter.SchemaVersion);
        Assert.Contains("\"schemaVersion\":10", Write(inEncounter), StringComparison.Ordinal);
        Assert.Contains("\"encounter\":{\"round\":0}", Write(inEncounter), StringComparison.Ordinal);

        var timedToggle = V8() with { Play = V8().Play with { Toggles = [new(Fixtures.Quickfoot.ContentId, "fixture-toggle", 1)] } };
        Assert.Equal(10, timedToggle.SchemaVersion);
        Assert.Contains("\"roundsLeft\":1", Write(timedToggle), StringComparison.Ordinal);

        // A toggle without a count is the v7 shape and adds nothing.
        var plainToggle = V8() with { Play = V8().Play with { Toggles = [new(Fixtures.Quickfoot.ContentId, "fixture-toggle")] } };
        Assert.Equal(8, plainToggle.SchemaVersion);
        Assert.DoesNotContain("roundsLeft", Write(plainToggle), StringComparison.Ordinal);
    }

    [Fact]
    public void Ending_the_encounter_and_clearing_the_counts_returns_the_character_to_its_lower_version()
    {
        var back = V10() with { Play = V10().Play with { Encounter = null, Toggles = [.. V10().Play.Toggles.Select(t => t with { RoundsLeft = null })] } };
        Assert.Equal(9, back.SchemaVersion);
        Assert.DoesNotContain("encounter", Write(back), StringComparison.Ordinal);
        Assert.DoesNotContain("roundsLeft", Write(back), StringComparison.Ordinal);
    }

    [Fact]
    public void A_v10_character_round_trips_byte_for_byte()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "srd521-courier-v10.json");
        var file = File.ReadAllText(path).ReplaceLineEndings("\n");
        var character = JsonSerializer.Deserialize<Character>(file, RulesJson.Options)!;

        Assert.Equal(10, character.SchemaVersion);
        Assert.Equal(new EncounterState(2), character.Play.Encounter);
        Assert.Equal(3, Assert.Single(character.Play.Toggles).RoundsLeft);
        Assert.Empty(character.Validate());
        var written = JsonSerializer.Serialize(character, RulesJson.Options).ReplaceLineEndings("\n") + "\n";
        Assert.Equal(file, written);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void An_encounter_round_out_of_bounds_is_invalid(int round)
    {
        var character = V9() with { Play = V9().Play with { Encounter = new(round) } };
        Assert.Contains("play.encounter-invalid", Codes(character));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(10_001)]
    public void Rounds_left_out_of_bounds_are_invalid(int roundsLeft)
    {
        var character = V9() with { Play = V9().Play with { Toggles = [new(Fixtures.Quickfoot.ContentId, "fixture-toggle", roundsLeft)] } };
        Assert.Contains("play.toggle-rounds-invalid", Codes(character));
    }

    [Fact]
    public void Values_at_the_bounds_are_valid_and_messages_name_no_text()
    {
        Assert.Empty((V9() with { Play = V9().Play with { Encounter = new(0) } }).Validate());
        Assert.Empty((V9() with { Play = V9().Play with { Encounter = new(PlayState.MaxRounds), Toggles = [new(Fixtures.Quickfoot.ContentId, "fixture-toggle", 0), new(Fixtures.Quickfoot.ContentId, "other", PlayState.MaxRounds)] } }).Validate());

        var bad = V9() with { Play = V9().Play with { Encounter = new(-1), Toggles = [new(Fixtures.Quickfoot.ContentId, "Testy-secret-marker", -3)] } };
        var problems = bad.Validate();
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.DoesNotContain("secret-marker", p.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_stored_v10_character_with_no_v10_data_reads_as_v9_or_v8()
    {
        var asV10 = (string json, int from) => json.Replace($"\"schemaVersion\":{from}", "\"schemaVersion\":10", StringComparison.Ordinal);
        Assert.Equal(9, JsonSerializer.Deserialize<Character>(asV10(Write(V9()), 9), RulesJson.Options)!.SchemaVersion);
        Assert.Equal(8, JsonSerializer.Deserialize<Character>(asV10(Write(V8()), 8), RulesJson.Options)!.SchemaVersion);
    }
}
