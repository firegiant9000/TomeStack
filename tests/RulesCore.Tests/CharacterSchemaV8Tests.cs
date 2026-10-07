using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>Character schema v8 (owner, 2026-10-06; LIVING_SPECS D19, D21, D22): currency, session notes, concentration.</summary>
public class CharacterSchemaV8Tests
{
    [Fact]
    public void A_v7_character_upcasts_to_v8_with_empty_currency_and_notes_and_no_concentration()
    {
        // The on-disk fixture is an old schema (schemaVersion 1); every older version upcasts to the current one on read.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "srd521-courier.json"));
        Assert.Contains("\"schemaVersion\": 1", json);
        var character = JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;
        Assert.Equal(8, character.SchemaVersion);
        Assert.Equal(new Currency(), character.Currency);
        Assert.Empty(character.Notes);
        Assert.Null(character.Play.Concentration);
        Assert.Empty(character.Validate());
    }

    [Fact]
    public void Currency_notes_and_concentration_round_trip_through_json()
    {
        var spell = new ContentReference(Guid.Parse("00000000-0000-4000-8000-000000000001"), Guid.Parse("00000000-0000-4000-8000-000000000002"));
        var character = Fixtures.Srd521Character() with
        {
            Currency = new(Cp: 3, Gp: 12),
            Notes = [new(Guid.Parse("00000000-0000-4000-8000-000000000003"), new DateOnly(2026, 10, 6), "Fixture session: met the Fixture Guild.", DateTimeOffset.Parse("2026-10-06T20:00:00Z"))],
            Play = new PlayState { Concentration = new(spell, "Fixture Ward", PendingSaveDc: 12) },
        };
        var written = JsonSerializer.Serialize(character, RulesJson.Options);
        Assert.Contains("\"date\": \"2026-10-06\"", written.Replace("\"date\":\"2026-10-06\"", "\"date\": \"2026-10-06\""));
        var copy = JsonSerializer.Deserialize<Character>(written, RulesJson.Options)!;
        Assert.Equal(character.Currency, copy.Currency);
        Assert.Equal(character.Notes, copy.Notes);
        Assert.Equal(character.Play.Concentration, copy.Play.Concentration);
        Assert.Empty(copy.Validate());
    }

    [Fact]
    public void Validation_bounds_currency_notes_and_the_pending_save_dc()
    {
        var bad = Fixtures.Srd521Character() with
        {
            Currency = new(Gp: Currency.MaxCoins + 1),
            Notes = [new(Guid.NewGuid(), new DateOnly(2026, 10, 6), "", DateTimeOffset.UtcNow)],
            Play = new PlayState { Concentration = new(new(Guid.NewGuid(), Guid.NewGuid()), "", PendingSaveDc: 5) },
        };
        var codes = bad.Validate().Select(d => d.Code).ToList();
        Assert.Contains("character.currency-out-of-range", codes);
        Assert.Contains("character.note-text-invalid", codes);
        Assert.Contains("play.concentration-invalid", codes);
        var many = Fixtures.Srd521Character() with { Notes = [.. Enumerable.Range(0, Character.MaxNotes + 1).Select(i => new SessionNote(Guid.NewGuid(), new DateOnly(2026, 1, 1), $"Note {i}", DateTimeOffset.UtcNow))] };
        Assert.Contains("character.notes-too-many", many.Validate().Select(d => d.Code));
        var oldNote = Fixtures.Srd521Character() with { Notes = [new(Guid.NewGuid(), new DateOnly(1850, 1, 1), "Fixture note", DateTimeOffset.UtcNow)] };
        Assert.Contains("character.note-date-invalid", oldNote.Validate().Select(d => d.Code));
    }

    [Fact]
    public void A_concentration_name_has_a_length_bound()
    {
        PlayState Concentrating(string name) => new() { Concentration = new(new(Guid.NewGuid(), Guid.NewGuid()), name) };
        Assert.Empty(Concentrating(new string('x', PlayState.MaxConcentrationNameLength)).Validate());
        Assert.Contains("play.concentration-invalid", Concentrating(new string('x', PlayState.MaxConcentrationNameLength + 1)).Validate().Select(d => d.Code));
    }

    [Fact]
    public void Starting_concentration_on_a_spell_with_a_very_long_name_clips_the_stored_name_rather_than_refusing()
    {
        var spell = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var started = Concentration.On(spell, new string('x', PlayState.MaxConcentrationNameLength + 50));
        Assert.Equal(PlayState.MaxConcentrationNameLength, started.Name.Length);
        Assert.Empty(new PlayState { Concentration = started }.Validate());
        Assert.Equal("Fixture Ward", Concentration.On(spell, "Fixture Ward").Name);
    }

    [Fact]
    public void A_null_note_entry_is_reported_as_an_empty_entry()
    {
        var json = JsonSerializer.Serialize(Fixtures.Srd521Character(), RulesJson.Options)
            .Replace("\"notes\":[]", "\"notes\":[null]")
            .Replace("\"notes\": []", "\"notes\": [null]");
        Assert.Contains("[null]", json);
        var character = JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;
        Assert.Equal("character.empty-entry", Assert.Single(character.Validate()).Code);
    }

    [Fact]
    public void Replacing_a_reference_moves_the_concentration_spell_too()
    {
        var (from, to) = (Fixtures.Veil, Fixtures.Spark);
        var character = Fixtures.Srd521Character() with { Play = new PlayState { Concentration = new(from, "Fixture Veil") } };
        Assert.Equal(to, character.ReplaceReference(from, to).Play.Concentration!.Spell);
    }
}
