using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Character schema v11, MECH-X item 4: <see cref="CompanionPlay.ContentId"/> keys the hit points of a companion a feature declares.
/// The version written is computed from the data (R73): a character with only hand companions stays at v9, byte for byte.
/// </summary>
public class CompanionPlaySchemaTests
{
    private static readonly Guid Wardens = Guid.Parse("6a7e00f4-0000-4000-8000-000000000001");

    private static readonly Guid WardenId = Guid.Parse("6a7e0c00-0000-4000-8000-000000000001");

    private static readonly Guid MuleId = Guid.Parse("6a7e0cb0-0000-4000-8000-0000000000c1");

    private static Character Handler() => Fixtures.Load("pm-handler-l3.json");

    private static Character V9() => Fixtures.Load("srd521-courier-v9.json");

    private static string Write(Character character) => JsonSerializer.Serialize(character, RulesJson.Compact);

    private static Character Read(string json) => JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;

    private static List<string> Codes(Character character) => [.. character.Validate().Select(d => d.Code)];

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static Character WithRows(params CompanionPlay[] rows) =>
        Handler() with { Play = Handler().Play with { Companions = rows } };

    // The SHA-256 of what the previous build wrote (captured at 72fd9325, before this change).
    [Fact]
    public void Characters_with_hand_companions_write_the_bytes_the_previous_build_wrote()
    {
        var courier = V9();
        var id = Guid.Parse("6a7e00f3-0000-4000-8000-0000000000c1");
        var withHand = courier with
        {
            Companions = [new Companion(id, "Fixture Mule", null, null, 11, null, null, null)],
            Play = courier.Play with { Companions = [new CompanionPlay(id, 3, 1)] },
        };
        Assert.Equal(9, withHand.SchemaVersion);
        Assert.Equal("007e67c740e7af3cd3a24b46eaea622cccd7dfe8a87a8a0dd59840c42ec6ad8e", Sha(Write(withHand)));
        Assert.Equal("d84886273ea3faf5246b055199bf7c12d39bea63f0d44150224189fc75e84e15", Sha(Write(Fixtures.Load("srd521-courier-v10.json"))));
    }

    [Fact]
    public void A_content_companion_row_makes_the_character_v11_and_round_trips()
    {
        var character = Handler();
        Assert.Equal(11, character.SchemaVersion);
        Assert.True(character.UsesV11Fields);
        var json = Write(character);
        Assert.Contains("\"schemaVersion\":11", json, StringComparison.Ordinal);
        var back = Read(json);
        Assert.Equal(11, back.SchemaVersion);
        Assert.Equal(2, back.Play.Companions!.Count);
        Assert.Null(back.Play.Companions.Single(p => p.Id == MuleId).ContentId);
        Assert.Equal(new CompanionPlay(WardenId, 4, 0, Wardens), back.Play.Companions.Single(p => p.ContentId is not null));
        Assert.Equal(json, Write(back));
        Assert.Empty(character.Validate());
    }

    [Fact]
    public void A_content_row_is_not_an_orphan()
    {
        Assert.DoesNotContain("character.companion-play-unknown", Codes(Handler()));
        var unknownHand = WithRows(new CompanionPlay(Guid.NewGuid(), 1, 0));
        Assert.Contains("character.companion-play-unknown", Codes(unknownHand));
    }

    [Fact]
    public void Duplicates_are_keyed_by_content_and_id()
    {
        var other = Guid.Parse("6a7e00f4-0000-4000-8000-000000000003");
        Assert.Empty(WithRows(new CompanionPlay(WardenId, 1, 0, Wardens), new CompanionPlay(WardenId, 2, 0, other)).Validate());
        Assert.Contains("play.companion-duplicate", Codes(WithRows(new CompanionPlay(WardenId, 1, 0, Wardens), new CompanionPlay(WardenId, 2, 0, Wardens))));
    }

    [Fact]
    public void Play_row_bounds()
    {
        Assert.Contains("play.companion-content-missing", Codes(WithRows(new CompanionPlay(WardenId, 1, 0, Guid.Empty))));
        CompanionPlay[] Rows(int n) => [.. Enumerable.Range(1, n).Select(i => new CompanionPlay(Guid.NewGuid(), 1, 0, Guid.NewGuid()))];
        Assert.DoesNotContain("play.companion-too-many", Codes(WithRows(Rows(PlayState.MaxCompanionRows))));
        Assert.Contains("play.companion-too-many", Codes(WithRows(Rows(PlayState.MaxCompanionRows + 1))));
    }

    [Fact]
    public void A_null_row_is_an_empty_entry()
    {
        var character = Handler() with { Play = Handler().Play with { Companions = [null!] } };
        Assert.False(character.UsesV11Fields);
        var codes = Codes(character);
        Assert.Equal(["character.empty-entry"], codes);
    }

    [Fact]
    public void Lowering_below_v11_drops_content_rows_and_keeps_hand_rows()
    {
        var character = Handler();
        var to10 = DownConvert.Below(character, 10);
        Assert.False(to10.Blocked);
        var change = Assert.Single(to10.Changes, c => c.Code == BelowCodes.CompanionContent);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Contains("1 content companions", change.Message, StringComparison.Ordinal);
        Assert.Equal(9, to10.Character.SchemaVersion); // the hand row is a v9 field
        Assert.Equal(MuleId, Assert.Single(to10.Character.Play.Companions!).Id);
        Assert.Empty(to10.Character.Validate());

        var to8 = DownConvert.Below(character, 8);
        Assert.False(to8.Blocked);
        Assert.Equal(8, to8.Character.SchemaVersion);
        Assert.Null(to8.Character.Companions);
        Assert.Null(to8.Character.Play.Companions);
        Assert.Empty(to8.Character.Validate());
    }

    [Fact]
    public void Lowering_a_character_with_mods_and_a_content_row_reports_both()
    {
        var keen = new ContentReference(Guid.Parse("6a7e00f3-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000101"));
        var blade = new ContentReference(Guid.Parse("6a7e00f3-0000-4000-8000-000000000011"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000111"));
        var character = Handler() with { Equipment = [new EquipmentEntry(blade, true, 1, Guid.NewGuid(), Mods: [keen])] };
        var to10 = DownConvert.Below(character, 10);
        Assert.Contains(to10.Changes, c => c.Code == BelowCodes.ItemMods);
        Assert.Contains(to10.Changes, c => c.Code == BelowCodes.CompanionContent);
        Assert.Equal(9, to10.Character.SchemaVersion); // the hand row is a v9 field
        Assert.Empty(to10.Character.Validate());
    }
}
