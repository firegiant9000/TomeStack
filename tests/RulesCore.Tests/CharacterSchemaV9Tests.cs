using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Character schema v9 (Wave 2, LIVING_SPECS D42): details, custom items and attacks, damage defences, companions, XP, and
/// equipment attunement and charges. The version written is computed from the data (R73): v9 only when a v9 field is set, so
/// an untouched v8 character stays byte-identical to what 0.5.0 wrote and 0.5.0 can still read it.
/// </summary>
public class CharacterSchemaV9Tests
{
    private static Character V9() => Fixtures.Load("srd521-courier-v9.json");

    private static Character V8() => Fixtures.Load("srd521-courier-v8.json");

    private static string Write(Character character) => JsonSerializer.Serialize(character, RulesJson.Compact);

    private static Character Read(string json) => JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;

    private static List<string> Codes(Character character) => [.. character.Validate().Select(d => d.Code)];

    private static string Text(int length) => new('x', length);

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}");

    private static CustomAttack Attack(int n = 1, string name = "Fixture strike", int toHit = 5, string? damage = "1d6+3", string? type = "slashing", string? note = null) =>
        new(Id(n), name, toHit, damage, type, note);

    private static CustomItem Item(int n = 1, string name = "Fixture rope", int quantity = 1, string? note = null) => new(Id(n), name, quantity, note, false);

    private static Companion Companion(int n = 1, string name = "Fixture hound", int hitPointMaximum = 11) => new(Id(n), name, null, null, hitPointMaximum, null, null, null);

    // ---- round trip and upcast ---------------------------------------------------------------------------------------

    [Fact]
    public void Every_v9_field_round_trips_and_the_fixture_is_valid()
    {
        var character = V9();
        Assert.Equal(9, character.SchemaVersion);
        Assert.Empty(character.Validate());
        Assert.Equal(900, character.Xp);
        Assert.Equal("Fixture neutral", character.Details!.Alignment);
        Assert.Equal("Grey", character.Details.Appearance!.Eyes);
        Assert.Null(character.Details.Appearance.Skin);
        Assert.Equal(["Common", "Fixture tongue"], character.Details.Languages);
        Assert.Equal(2, character.CustomItems!.Count);
        Assert.Equal(("Fixture sling", 4, "1d4+2", "bludgeoning"), (character.CustomAttacks![0].Name, character.CustomAttacks[0].ToHit, character.CustomAttacks[0].Damage, character.CustomAttacks[0].DamageType));
        Assert.Equal(new DamageDefence("poison", DamageDefenceKind.Immunity), character.DamageDefences![1]);
        Assert.Equal(11, character.Companions![0].HitPointMaximum);
        Assert.Single(character.Companions[0].Attacks!);
        Assert.Equal(new CompanionPlay(character.Companions[0].Id, 7, 0), character.Play.Companions![0]);

        var written = Write(character);
        Assert.Contains("\"schemaVersion\":9", written, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"immunity\"", written, StringComparison.Ordinal);
        var copy = Read(written);
        Assert.Equal(written, Write(copy));
        Assert.Equal(9, copy.SchemaVersion);
        Assert.Empty(copy.Validate());
    }

    [Fact]
    public void Equipment_id_attunement_and_charges_round_trip()
    {
        var character = V8() with { Equipment = [new(Fixtures.Spark, true, 2, Id(7), true, 3), new(Fixtures.Veil)] };
        var copy = Read(Write(character));
        Assert.Equal((Id(7), true, 3), (copy.Equipment[0].Id, copy.Equipment[0].Attuned, copy.Equipment[0].ChargesSpent));
        Assert.Null(copy.Equipment[1].Id);
        Assert.Null(copy.Equipment[1].Attuned);
        Assert.Null(copy.Equipment[1].ChargesSpent);
        Assert.Equal(9, copy.SchemaVersion);
        Assert.Equal(1, Write(character).Split("attuned").Length - 1); // nothing is written for an absent value
        Assert.Empty(copy.Validate());
    }

    [Fact]
    public void A_v8_and_an_older_fixture_upcast_with_every_v9_field_absent()
    {
        foreach (var fixture in new[] { "srd521-courier-v8.json", "srd521-courier.json", "srd51-quickfoot.json" })
        {
            var character = Fixtures.Load(fixture);
            Assert.Equal(8, character.SchemaVersion);
            Assert.Null(character.Details);
            Assert.Null(character.CustomItems);
            Assert.Null(character.CustomAttacks);
            Assert.Null(character.DamageDefences);
            Assert.Null(character.Companions);
            Assert.Null(character.Xp);
            Assert.Null(character.Play.Companions);
            Assert.All(character.Equipment, e => Assert.True(e.Id is null && e.Attuned is null && e.ChargesSpent is null));
            Assert.Empty(character.Validate());
        }
    }

    [Fact]
    public void A_stored_v9_character_with_no_v9_data_reads_as_v8_and_a_newer_version_is_still_refused()
    {
        var json = Write(V8()).Replace("\"schemaVersion\":8", "\"schemaVersion\":9", StringComparison.Ordinal);
        Assert.Equal(8, Read(json).SchemaVersion);
        var future = Read(Write(V8()).Replace("\"schemaVersion\":8", "\"schemaVersion\":10", StringComparison.Ordinal));
        Assert.Equal(10, future.SchemaVersion);
        Assert.Contains("character.schema-unsupported", Codes(future));
    }

    // ---- the version written (R73, R74) --------------------------------------------------------------------------------

    [Fact]
    public void Saving_a_character_with_no_v9_field_writes_schema_8_and_the_bytes_0_5_0_wrote()
    {
        // tests/RulesFixtures/upgrade/v0.5.0: the exact JSON a v0.5.0 build stored for two invented characters.
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "upgrade", "v0.5.0"), "stored-*.json");
        Assert.Equal(2, files.Length);
        foreach (var file in files)
        {
            var stored = File.ReadAllText(file);
            Assert.Contains("\"schemaVersion\":8", stored, StringComparison.Ordinal);
            Assert.Equal(stored, Write(Read(stored)));
        }
    }

    public static TheoryData<string> SingleV9Fields() =>
        ["details", "customItems", "customAttacks", "damageDefences", "companions", "xp", "equipmentId", "equipmentAttuned", "equipmentCharges", "companionPlay"];

    [Theory]
    [MemberData(nameof(SingleV9Fields))]
    public void One_v9_field_alone_writes_schema_9(string field)
    {
        var plain = V8() with { Equipment = [new(Fixtures.Spark)] };
        Assert.Equal(8, plain.SchemaVersion);
        var character = field switch
        {
            "details" => plain with { Details = new(Alignment: "Fixture neutral") },
            "customItems" => plain with { CustomItems = [Item()] },
            "customAttacks" => plain with { CustomAttacks = [Attack()] },
            "damageDefences" => plain with { DamageDefences = [new("fire", DamageDefenceKind.Resistance)] },
            "companions" => plain with { Companions = [Companion()] },
            "xp" => plain with { Xp = 0 },
            "equipmentId" => plain with { Equipment = [new(Fixtures.Spark, Id: Id(1))] },
            "equipmentAttuned" => plain with { Equipment = [new(Fixtures.Spark, Attuned: true)] },
            "equipmentCharges" => plain with { Equipment = [new(Fixtures.Spark, ChargesSpent: 1)] },
            "companionPlay" => plain with { Play = plain.Play with { Companions = [new(Id(1), null, 0)] } },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.Equal(9, character.SchemaVersion);
        Assert.Contains("\"schemaVersion\":9", Write(character), StringComparison.Ordinal);
    }

    [Fact]
    public void Attuned_false_and_zero_charges_spent_are_the_same_as_absent_so_the_character_stays_v8()
    {
        var plain = V8() with { Equipment = [new(Fixtures.Spark)] };
        var off = plain with { Equipment = [new(Fixtures.Spark, Attuned: false, ChargesSpent: 0)] };
        Assert.Null(off.Equipment[0].Attuned);
        Assert.Null(off.Equipment[0].ChargesSpent);
        Assert.Equal(8, off.SchemaVersion);
        var written = Write(off);
        Assert.Contains("\"schemaVersion\":8", written, StringComparison.Ordinal);
        Assert.DoesNotContain("attuned", written, StringComparison.Ordinal);
        Assert.DoesNotContain("chargesSpent", written, StringComparison.Ordinal);
        Assert.Equal(Write(plain), written);

        // Read from JSON the same way (a v9 file written by a build that kept the "off" values).
        var read = Read(Write(plain).Replace("\"quantity\":1", "\"quantity\":1,\"attuned\":false,\"chargesSpent\":0", StringComparison.Ordinal));
        Assert.Equal(8, read.SchemaVersion);
        Assert.Equal(Write(plain), Write(read));

        // Turning attunement on writes 9; so do spent charges; turning it off again returns to 8.
        var on = plain with { Equipment = [plain.Equipment[0] with { Attuned = true }] };
        Assert.Equal(9, on.SchemaVersion);
        Assert.Contains("\"attuned\":true", Write(on), StringComparison.Ordinal);
        Assert.Equal(9, (plain with { Equipment = [plain.Equipment[0] with { ChargesSpent = 2 }] }).SchemaVersion);
        Assert.Equal(8, (on with { Equipment = [on.Equipment[0] with { Attuned = false }] }).SchemaVersion);
    }

    [Fact]
    public void An_empty_id_is_refused_wherever_an_id_names_an_entry()
    {
        var baseline = V8();
        AssertRefused("character.custom-item-id-missing", baseline with { CustomItems = [new(Guid.Empty, "Fixture rope", 1, null, false)] }, "item");
        AssertRefused("character.custom-attack-id-missing", baseline with { CustomAttacks = [new(Guid.Empty, "Fixture strike", 1, null, null, null)] }, "attack");
        AssertRefused("character.companion-id-missing", baseline with { Companions = [Companion() with { Id = Guid.Empty }] }, "companion");
        AssertRefused("character.custom-attack-id-missing", baseline with { Companions = [Companion() with { Attacks = [new(Guid.Empty, "Fixture bite", 1, null, null, null)] }] }, "companion attack");
        AssertRefused("character.equipment-id-missing", baseline with { Equipment = [new(Fixtures.Spark, Id: Guid.Empty)] }, "equipment");
        AssertRefused("play.companion-id-missing", baseline with { Companions = [Companion()], Play = baseline.Play with { Companions = [new(Guid.Empty, null, 0)] } }, "companion play");

        // A missing id in JSON deserialises to the empty id, so it is refused the same way.
        var node = System.Text.Json.Nodes.JsonNode.Parse(Write(V9()))!;
        node["customItems"]![0]!.AsObject().Remove("id");
        Assert.Contains("character.custom-item-id-missing", Codes(Read(node.ToJsonString())));
    }

    [Fact]
    public void The_v9_maxima_add_up_to_the_ruled_budget_of_about_100_000_characters()
    {
        var attack = Character.MaxName + DiceLimits.MaxLength + Character.MaxDamageTypeLength + Character.MaxShortNote;
        var companion = Character.MaxName + Character.MaxCompanionTextLength + Character.MaxCompanionSpeedLength + Character.MaxShortNote + (Character.MaxCompanionAttacks * attack);
        var total = (13 * Character.MaxDetailText)
            + (2 * Character.MaxDetailListEntries * Character.MaxDetailListEntryLength)
            + (Character.MaxCustomItems * (Character.MaxName + Character.MaxShortNote))
            + (Character.MaxCustomAttacks * attack)
            + (Character.MaxCompanions * companion);
        Assert.InRange(total, 1, 100_000);
    }

    [Fact]
    public void Empty_v9_lists_and_all_empty_details_are_normalised_to_absent_so_the_character_stays_v8()
    {
        var empty = V8() with
        {
            Details = new() { Appearance = new(), Languages = [], ToolsAndOther = [], Backstory = "" },
            CustomItems = [],
            CustomAttacks = [],
            DamageDefences = [],
            Companions = [],
            Play = V8().Play with { Companions = [] },
        };
        Assert.Equal(8, empty.SchemaVersion);
        var written = Write(empty);
        Assert.Contains("\"schemaVersion\":8", written, StringComparison.Ordinal);
        foreach (var key in new[] { "details", "customItems", "customAttacks", "damageDefences", "companions", "xp" })
            Assert.DoesNotContain($"\"{key}\"", written, StringComparison.Ordinal);
        Assert.Equal(Write(V8()), written);
    }

    [Fact]
    public void Removing_the_last_v9_value_makes_the_character_v8_again()
    {
        var added = V8() with
        {
            Details = new(Alignment: "Fixture neutral", Languages: ["Common"]),
            CustomItems = [Item()],
            CustomAttacks = [Attack()],
            DamageDefences = [new("fire", DamageDefenceKind.Resistance)],
            Companions = [Companion()],
            Xp = 10,
            Equipment = [new(Fixtures.Spark, Attuned: true)],
            Play = V8().Play with { Companions = [new(Id(1), 3, 0)] },
        };
        Assert.Equal(9, added.SchemaVersion);
        var removed = added with
        {
            Details = added.Details! with { Alignment = null, Languages = [] }, // the last value of the page goes
            CustomItems = [],
            CustomAttacks = null,
            DamageDefences = [],
            Companions = null,
            Xp = null,
            Equipment = [new(Fixtures.Spark)],
            Play = V8().Play,
        };
        Assert.Equal(8, removed.SchemaVersion);
        Assert.Equal(Write(V8() with { Equipment = [new(Fixtures.Spark)] }), Write(removed));
    }

    [Fact]
    public void Damage_defence_types_are_stored_lower_case()
    {
        var json = Write(V9()).Replace("\"type\":\"fire\"", "\"type\":\"FIRE\"", StringComparison.Ordinal);
        var character = Read(json);
        Assert.Equal("fire", character.DamageDefences![0].Type);
        Assert.Empty(character.Validate());
        Assert.Equal("cold", (V8() with { DamageDefences = [new("Cold", DamageDefenceKind.Vulnerability)] }).DamageDefences![0].Type);
    }

    [Fact]
    public void The_damage_types_are_the_closed_list_of_both_srds()
    {
        Assert.Equal(13, DamageTypes.All.Count);
        Assert.Equal(DamageTypes.All.Order(StringComparer.Ordinal), DamageTypes.All);
        Assert.Equal(DamageTypes.All, DamageTypes.All.Select(t => t.ToLowerInvariant()));
    }

    // ---- validation -----------------------------------------------------------------------------------------------

    [Fact]
    public void Values_at_every_maximum_are_accepted()
    {
        var details = new CharacterDetails(
            Alignment: Text(2000), PersonalityTraits: Text(2000), Ideals: Text(2000), Bonds: Text(2000), Flaws: Text(2000), Backstory: Text(2000), AlliesAndOrganisations: Text(2000),
            Appearance: new(Text(2000), Text(2000), Text(2000), Text(2000), Text(2000), Text(2000)),
            Languages: [.. Enumerable.Range(0, 30).Select(_ => Text(80))], ToolsAndOther: [.. Enumerable.Range(0, 30).Select(_ => Text(80))]);
        var companions = Enumerable.Range(0, 10).Select(i => new Companion(Id(100 + i), Text(80), Text(80), 50, 10_000, Text(80), [.. Enumerable.Range(0, 5).Select(a => Attack(1000 + (i * 10) + a, Text(80), 30, "8d100+1000", Text(80), Text(200)))], Text(200))).ToList();
        var character = V8() with
        {
            Details = details,
            CustomItems = [.. Enumerable.Range(0, 50).Select(i => Item(i + 1, Text(80), 9_999, Text(200)))],
            CustomAttacks = [.. Enumerable.Range(0, 30).Select(i => Attack(i + 1, toHit: i == 0 ? -20 : 30))],
            DamageDefences = [.. DamageTypes.All.Select(t => new DamageDefence(t, DamageDefenceKind.Resistance))],
            Companions = companions,
            Xp = 1_000_000,
            Equipment = [new(Fixtures.Spark, ChargesSpent: 1000, Id: Id(1))],
            Play = new PlayState { Companions = [.. companions.Select(c => new CompanionPlay(c.Id, 10_000, 10_000))] },
        };
        Assert.Empty(character.Validate());
    }

    private static void AssertRefused(string code, Character character, string because)
    {
        var codes = Codes(character);
        Assert.True(codes.Contains(code), $"{because}: expected {code}, got [{string.Join(", ", codes)}]");
    }

    [Fact]
    public void Each_detail_bound_is_refused_with_its_own_code()
    {
        var baseline = V8();
        var tooLong = Text(2001);
        AssertRefused("character.details-text-too-long", baseline with { Details = new(Alignment: tooLong) }, "alignment");
        AssertRefused("character.details-text-too-long", baseline with { Details = new(Backstory: tooLong) }, "backstory");
        AssertRefused("character.details-text-too-long", baseline with { Details = new(AlliesAndOrganisations: tooLong) }, "allies");
        AssertRefused("character.details-text-too-long", baseline with { Details = new(Appearance: new(Hair: tooLong)) }, "hair");
        AssertRefused("character.details-list-too-many", baseline with { Details = new(Languages: [.. Enumerable.Repeat("a", 31)]) }, "31 languages");
        AssertRefused("character.details-list-too-many", baseline with { Details = new(ToolsAndOther: [.. Enumerable.Repeat("a", 31)]) }, "31 tools");
        AssertRefused("character.details-list-entry-invalid", baseline with { Details = new(Languages: [Text(81)]) }, "81-character language");
        AssertRefused("character.details-list-entry-invalid", baseline with { Details = new(ToolsAndOther: [" "]) }, "blank tool");
    }

    [Fact]
    public void Each_custom_item_bound_is_refused_with_its_own_code()
    {
        var baseline = V8();
        AssertRefused("character.custom-items-too-many", baseline with { CustomItems = [.. Enumerable.Range(1, 51).Select(i => Item(i))] }, "51 items");
        AssertRefused("character.custom-item-name-invalid", baseline with { CustomItems = [Item(name: Text(81))] }, "long name");
        AssertRefused("character.custom-item-name-invalid", baseline with { CustomItems = [Item(name: " ")] }, "blank name");
        AssertRefused("character.custom-item-note-too-long", baseline with { CustomItems = [Item(note: Text(201))] }, "long note");
        AssertRefused("character.custom-item-quantity", baseline with { CustomItems = [Item(quantity: 0)] }, "quantity 0");
        AssertRefused("character.custom-item-quantity", baseline with { CustomItems = [Item(quantity: 10_000)] }, "quantity 10000");
        AssertRefused("character.custom-item-duplicate", baseline with { CustomItems = [Item(1), Item(1)] }, "duplicate id");
    }

    [Fact]
    public void Each_custom_attack_bound_is_refused_with_its_own_code()
    {
        var baseline = V8();
        AssertRefused("character.custom-attacks-too-many", baseline with { CustomAttacks = [.. Enumerable.Range(1, 31).Select(i => Attack(i))] }, "31 attacks");
        AssertRefused("character.custom-attack-name-invalid", baseline with { CustomAttacks = [Attack(name: "")] }, "blank name");
        AssertRefused("character.custom-attack-name-invalid", baseline with { CustomAttacks = [Attack(name: Text(81))] }, "long name");
        AssertRefused("character.custom-attack-note-too-long", baseline with { CustomAttacks = [Attack(note: Text(201))] }, "long note");
        AssertRefused("character.custom-attack-type-too-long", baseline with { CustomAttacks = [Attack(type: Text(81))] }, "long type");
        AssertRefused("character.custom-attack-to-hit-out-of-range", baseline with { CustomAttacks = [Attack(toHit: -21)] }, "to hit -21");
        AssertRefused("character.custom-attack-to-hit-out-of-range", baseline with { CustomAttacks = [Attack(toHit: 31)] }, "to hit 31");
        AssertRefused("character.custom-attack-damage-invalid", baseline with { CustomAttacks = [Attack(damage: "banana")] }, "not dice");
        AssertRefused("character.custom-attack-damage-invalid", baseline with { CustomAttacks = [Attack(damage: "101d6")] }, "too many dice");
        AssertRefused("character.custom-attack-damage-invalid", baseline with { CustomAttacks = [Attack(damage: "1d1001")] }, "too many sides");
        AssertRefused("character.custom-attack-damage-invalid", baseline with { CustomAttacks = [Attack(damage: "1d6+" + Text(40).Replace('x', '1'))] }, "too long");
        AssertRefused("character.custom-attack-duplicate", baseline with { CustomAttacks = [Attack(1), Attack(1)] }, "duplicate id");
        Assert.Empty(baseline.Validate());
        Assert.Empty((baseline with { CustomAttacks = [Attack(damage: null, type: null)] }).Validate()); // a note-only attack is fine
    }

    [Fact]
    public void Each_damage_defence_bound_is_refused_with_its_own_code()
    {
        var baseline = V8();
        AssertRefused("character.damage-defence-type-unknown", baseline with { DamageDefences = [new("sonic", DamageDefenceKind.Resistance)] }, "unknown type");
        AssertRefused("character.damage-defence-duplicate", baseline with { DamageDefences = [new("fire", DamageDefenceKind.Resistance), new("FIRE", DamageDefenceKind.Resistance)] }, "duplicate");
        Assert.DoesNotContain("character.damage-defence-duplicate", Codes(baseline with { DamageDefences = [new("fire", DamageDefenceKind.Resistance), new("fire", DamageDefenceKind.Vulnerability)] })); // different kinds are two entries
    }

    [Fact]
    public void Each_companion_bound_is_refused_with_its_own_code()
    {
        var baseline = V8();
        AssertRefused("character.companions-too-many", baseline with { Companions = [.. Enumerable.Range(1, 11).Select(i => Companion(i))] }, "11 companions");
        AssertRefused("character.companion-name-invalid", baseline with { Companions = [Companion(name: "")] }, "blank name");
        AssertRefused("character.companion-name-invalid", baseline with { Companions = [Companion(name: Text(81))] }, "long name");
        AssertRefused("character.companion-text-too-long", baseline with { Companions = [Companion() with { Kind = Text(81) }] }, "long kind");
        AssertRefused("character.companion-text-too-long", baseline with { Companions = [Companion() with { Speed = Text(81) }] }, "long speed");
        AssertRefused("character.companion-text-too-long", baseline with { Companions = [Companion() with { Note = Text(201) }] }, "long note");
        AssertRefused("character.companion-armor-class-out-of-range", baseline with { Companions = [Companion() with { ArmorClass = 51 }] }, "AC 51");
        AssertRefused("character.companion-armor-class-out-of-range", baseline with { Companions = [Companion() with { ArmorClass = -1 }] }, "AC -1");
        AssertRefused("character.companion-hit-points-out-of-range", baseline with { Companions = [Companion(hitPointMaximum: -1)] }, "hp -1");
        AssertRefused("character.companion-hit-points-out-of-range", baseline with { Companions = [Companion(hitPointMaximum: 10_001)] }, "hp 10001");
        AssertRefused("character.companion-attacks-too-many", baseline with { Companions = [Companion() with { Attacks = [.. Enumerable.Range(1, 6).Select(i => Attack(i))] }] }, "6 attacks");
        AssertRefused("character.custom-attack-to-hit-out-of-range", baseline with { Companions = [Companion() with { Attacks = [Attack(toHit: 40)] }] }, "companion attack to hit");
        AssertRefused("character.custom-attack-damage-invalid", baseline with { Companions = [Companion() with { Attacks = [Attack(damage: "x")] }] }, "companion attack damage");
        AssertRefused("character.companion-duplicate", baseline with { Companions = [Companion(1), Companion(1)] }, "duplicate id");
    }

    [Fact]
    public void Companion_play_state_is_bounded_and_must_name_a_companion()
    {
        var baseline = V8() with { Companions = [Companion(1)] };
        AssertRefused("play.companion-invalid", baseline with { Play = new PlayState { Companions = [new(Id(1), -1, 0)] } }, "current -1");
        AssertRefused("play.companion-invalid", baseline with { Play = new PlayState { Companions = [new(Id(1), 10_001, 0)] } }, "current 10001");
        AssertRefused("play.companion-invalid", baseline with { Play = new PlayState { Companions = [new(Id(1), null, -1)] } }, "temporary -1");
        AssertRefused("play.companion-invalid", baseline with { Play = new PlayState { Companions = [new(Id(1), null, 10_001)] } }, "temporary 10001");
        AssertRefused("play.companion-duplicate", baseline with { Play = new PlayState { Companions = [new(Id(1), null, 0), new(Id(1), 3, 0)] } }, "duplicate");
        AssertRefused("character.companion-play-unknown", baseline with { Play = new PlayState { Companions = [new(Id(2), null, 0)] } }, "no such companion");
        Assert.Empty((baseline with { Play = new PlayState { Companions = [new(Id(1), 4, 2)] } }).Validate());
    }

    [Fact]
    public void Xp_and_equipment_charges_are_bounded()
    {
        var baseline = V8();
        AssertRefused("character.xp-out-of-range", baseline with { Xp = -1 }, "xp -1");
        AssertRefused("character.xp-out-of-range", baseline with { Xp = 1_000_001 }, "xp 1000001");
        AssertRefused("character.equipment-charges-out-of-range", baseline with { Equipment = [new(Fixtures.Spark, ChargesSpent: -1)] }, "charges -1");
        AssertRefused("character.equipment-charges-out-of-range", baseline with { Equipment = [new(Fixtures.Spark, ChargesSpent: 1001)] }, "charges 1001");
        AssertRefused("character.equipment-id-duplicate", baseline with { Equipment = [new(Fixtures.Spark, Id: Id(1)), new(Fixtures.Veil, Id: Id(1))] }, "duplicate entry id");
    }

    [Fact]
    public void Messages_name_the_field_and_limit_never_the_text()
    {
        const string Secret = "Testy-secret-marker";
        var bad = V8() with
        {
            Details = new(Alignment: Secret + Text(2001), Languages: [Secret + Text(81)]),
            CustomItems = [Item(name: Secret + Text(121)), Item(2, quantity: 0)],
            CustomAttacks = [Attack(name: Secret + Text(121), damage: Secret, type: Secret + Text(81), toHit: 99)],
            DamageDefences = [new(Secret, DamageDefenceKind.Immunity)],
            Companions = [Companion(name: Secret + Text(121)) with { Kind = Secret + Text(121) }],
        };
        var problems = bad.Validate();
        Assert.True(problems.Count >= 8);
        Assert.All(problems, p => Assert.DoesNotContain("secret-marker", p.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_null_entry_in_a_v9_list_is_reported_as_an_empty_entry_not_thrown()
    {
        foreach (var path in new[] { new[] { "customItems" }, new[] { "customAttacks" }, new[] { "damageDefences" }, new[] { "companions" }, new[] { "play", "companions" } })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Write(V9()))!;
            var parent = path.Length == 1 ? node : node[path[0]]!;
            parent[path[^1]]!.AsArray().Insert(0, null);
            Assert.Equal("character.empty-entry", Assert.Single(Read(node.ToJsonString()).Validate()).Code);
        }
    }

    [Fact]
    public void Replacing_a_reference_keeps_v9_data_and_equipment_ids()
    {
        var character = V8() with { Equipment = [new(Fixtures.Spark, true, 1, Id(9), true, 2)], Xp = 300 };
        var moved = character.ReplaceReference(Fixtures.Spark, Fixtures.Veil);
        Assert.Equal((Fixtures.Veil, Id(9), true, 2, 300), (moved.Equipment[0].Item, moved.Equipment[0].Id, moved.Equipment[0].Attuned, moved.Equipment[0].ChargesSpent, moved.Xp));
    }
}
