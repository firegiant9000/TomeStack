using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Character schema v11 (MECH-X item 3, D75): the mods attached to one equipment entry. The version written is computed from the data
/// (R73), four tiers: v11 only while an entry has a mod, otherwise v10, v9 or v8, so a character that carries no mods stays readable
/// by the previous release.
/// </summary>
public class CharacterSchemaV11Tests
{
    private static readonly ContentReference Longblade = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000011"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000111"));

    private static readonly ContentReference KeenRune = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000101"));

    private static Character V11() => Fixtures.Load("srd521-courier-v11.json");

    private static Character V10() => Fixtures.Load("srd521-courier-v10.json");

    private static Character V9() => Fixtures.Load("srd521-courier-v9.json");

    private static Character V8() => Fixtures.Load("srd521-courier-v8.json");

    private static string Write(Character character) => JsonSerializer.Serialize(character, RulesJson.Compact);

    private static Character Read(string json) => JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;

    private static List<string> Codes(Character character) => [.. character.Validate().Select(d => d.Code)];

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // The SHA-256 of what the previous build wrote for these characters (captured at 85739258, before this change).
    [Theory]
    [InlineData("srd521-courier-v10.json", "d84886273ea3faf5246b055199bf7c12d39bea63f0d44150224189fc75e84e15")]
    [InlineData("srd521-courier-v9.json", "13f923e928c70a663b167187cab3f0b198715299953a50d7e56fc8d259cd273b")]
    [InlineData("srd521-courier-v8.json", "2548832a03294bc9448fc433fc59a546886f4bde66299cfe27403c3f5b8be822")]
    [InlineData("pm-marker-l5.json", "a733af93340932f39587cb23a103597a747ea4deb5251cad8deecf87ebbaa32e")]
    [InlineData("pm-instinct-l4.json", "844d944f0323b06bf11022c632a0f11df2bea026ba1f13ec31827d64c46a1a3a")]
    [InlineData("pm-wave-adept-l5.json", "7bd96f9591456000b4a0eb1e765c1003b6ffe75d01f29e1090199d5329d79471")]
    [InlineData("pm-adept-l5.json", "4df8dd5095fbec8dd785d55fee1339e78ea2f148a0cd1efaa2da8aed16432a68")]
    public void Characters_without_mods_write_the_bytes_the_previous_build_wrote(string file, string sha256)
    {
        Assert.Equal(sha256, Sha(Write(Fixtures.Load(file))));
    }

    [Fact]
    public void A_character_with_a_mod_is_written_at_v11_and_round_trips()
    {
        var character = V11();
        Assert.Equal(11, character.SchemaVersion);
        Assert.True(character.UsesV11Fields);
        var json = Write(character);
        Assert.Contains("\"schemaVersion\":11", json, StringComparison.Ordinal);
        Assert.Contains("\"mods\":[", json, StringComparison.Ordinal);
        var back = Read(json);
        Assert.Equal(11, back.SchemaVersion);
        Assert.Single(back.Equipment[0].Mods!);
        Assert.Equal(KeenRune, back.Equipment[0].Mods![0]);
        Assert.Equal(json, Write(back));
        Assert.Empty(character.Validate());
    }

    [Fact]
    public void An_empty_mods_list_is_null_and_stays_v10()
    {
        var viaConstructor = new EquipmentEntry(Longblade, true, 1, Guid.NewGuid(), Mods: []);
        Assert.Null(viaConstructor.Mods);
        var viaInit = viaConstructor with { Mods = [] };
        Assert.Null(viaInit.Mods);
        var character = V10() with { Equipment = [viaConstructor, viaInit with { Id = Guid.NewGuid() }] };
        Assert.False(character.UsesV11Fields);
        Assert.Equal(10, character.SchemaVersion);
        Assert.DoesNotContain("mods", Write(character), StringComparison.Ordinal);
        var stored = JsonSerializer.Deserialize<EquipmentEntry>("""{"item":{"contentId":"6a7e00f3-0000-4000-8000-000000000011","revisionId":"6a7e00f3-0000-4000-8000-000000000111"},"mods":[]}""", RulesJson.Options)!;
        Assert.Null(stored.Mods);
    }

    [Fact]
    public void A_v12_character_is_refused_and_the_constants_name_the_rungs()
    {
        var future = Read(Write(V11()).Replace("\"schemaVersion\":11", "\"schemaVersion\":12", StringComparison.Ordinal));
        Assert.Equal(12, future.SchemaVersion);
        Assert.Contains("character.schema-unsupported", Codes(future));

        Assert.Equal(11, Character.CurrentSchemaVersion);
        Assert.Equal(11, Character.ItemModsSchemaVersion);
        Assert.Equal(10, Character.EncounterSchemaVersion);
        Assert.Equal(9, Character.DetailsSchemaVersion);
        Assert.Equal(8, Character.PreviousSchemaVersion);
    }

    [Fact]
    public void Mod_bounds()
    {
        var entry = V11().Equipment[0];
        Assert.Equal(4, EquipmentEntry.MaxMods);

        ContentReference Extra(int n) => new(Guid.Parse($"6a7e00f3-0000-4000-8000-0000000009{n:D2}"), Guid.Parse($"6a7e00f3-0000-4000-8000-0000000019{n:D2}"));
        var four = V11() with { Equipment = [entry with { Mods = [KeenRune, Extra(1), Extra(2), Extra(3)] }] };
        Assert.Empty(four.Validate());

        var five = V11() with { Equipment = [entry with { Mods = [KeenRune, Extra(1), Extra(2), Extra(3), Extra(4)] }] };
        Assert.Equal(["character.equipment-mods-too-many"], Codes(five));

        var twice = V11() with { Equipment = [entry with { Mods = [KeenRune, KeenRune] }] };
        Assert.Equal(["character.equipment-mod-duplicate"], Codes(twice));

        var self = V11() with { Equipment = [entry with { Mods = [Longblade] }] };
        Assert.Equal(["character.equipment-mod-self"], Codes(self));

        var all = V11() with { Equipment = [entry with { Mods = [Longblade, Longblade, Extra(1), Extra(2), Extra(3)] }] };
        Assert.All(all.Validate(), p => Assert.DoesNotContain("Longblade", p.Message, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("1", all.Validate()[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_inside_mods_is_an_empty_entry()
    {
        var entry = V11().Equipment[0];
        var character = V11() with { Equipment = [entry with { Mods = [KeenRune, null!] }] };
        var problems = character.Validate();
        Assert.Equal(["character.empty-entry"], [.. problems.Select(p => p.Code)]);
        Assert.True(character.UsesV11Fields);
    }

    [Fact]
    public void AllReferences_and_ReplaceReference_cover_mods()
    {
        var character = V11();
        Assert.Contains(KeenRune, character.AllReferences());
        Assert.Contains(Longblade, character.AllReferences());

        var next = new ContentReference(KeenRune.ContentId, Guid.Parse("6a7e00f3-0000-4000-8000-000000000999"));
        var swapped = character.ReplaceReference(KeenRune, next);
        Assert.Equal([next], swapped.Equipment[0].Mods!);
        Assert.DoesNotContain(KeenRune, swapped.AllReferences());
        Assert.Equal(Longblade, swapped.Equipment[0].Item);

        var plain = V10().ReplaceReference(KeenRune, next);
        Assert.Equal(Write(V10()), Write(plain));
    }

    [Fact]
    public void A_stored_v11_character_with_no_mods_reads_as_v10_v9_or_v8()
    {
        var asV11 = (string json, int from) => json.Replace($"\"schemaVersion\":{from}", "\"schemaVersion\":11", StringComparison.Ordinal);
        Assert.Equal(10, Read(asV11(Write(V10()), 10)).SchemaVersion);
        Assert.Equal(9, Read(asV11(Write(V9()), 9)).SchemaVersion);
        Assert.Equal(8, Read(asV11(Write(V8()), 8)).SchemaVersion);
    }
}
