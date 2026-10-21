using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v10 (Wave 3, item 24, D42, R155): every v10 key defined whole, with its type, bounds and version gate.
/// The version gate itself (a v9 revision carrying these keys) is in <see cref="ContentSchemaV10GateTests"/>.
/// </summary>
public class ContentSchemaV10Tests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static ContentRevision Draft(ContentKind kind, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("c10a0000-0000-4000-8000-000000000002"), RevisionId = Guid.Parse("c10b0000-0000-4000-8000-000000000002"),
        Kind = kind, Name = "Fixture V10", RulesFamilies = [RulesFamilies.Srd521], Provenance = new(Guid.Parse("5f0d5001-0000-4000-8000-000000000001"), new PageRef(30)),
        Status = RevisionStatus.Draft, Effects = effects,
    };

    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Catalog);

    private static IEnumerable<string> Errors(ContentRevision revision) => Validate(revision).Errors.Select(e => e.Code);

    private static ContentRevision RoundTrip(ContentRevision revision) =>
        JsonSerializer.Deserialize<ContentRevision>(JsonSerializer.Serialize(revision, RulesJson.Compact), RulesJson.Compact)!;

    private static ToggleEffect Toggle() => new() { Id = "t", ToggleId = "stance", Label = "Fixture Stance" };

    private static ResourceEffect Pool() => new() { Id = "p", ResourceId = "pool", Label = "Fixture Pool", Maximum = "2" };

    private static GrantEffect Resist(string? damageType) => new() { Id = "g", Grant = GrantKind.DamageResistance, DamageType = damageType };

    private static ItemEffect ChargedItem(int max = 3, ItemChargeRecovery? recovery = null) =>
        new() { Id = "i", RequiresAttunement = true, Charges = new ItemCharges { Max = max, Recovery = recovery } };

    // ---- the version constant ---------------------------------------------------------------------------------------

    [Fact]
    public void The_current_content_schema_is_v10_and_a_new_draft_is_written_as_v10()
    {
        Assert.Equal(10, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(10, Draft(ContentKind.Feat).SchemaVersion);
    }

    // ---- round trips ------------------------------------------------------------------------------------------------

    [Fact]
    public void Toggle_autoWhen_and_activation_round_trip_in_a_v10_revision()
    {
        var toggle = Toggle() with { AutoWhen = ToggleAutoWhen.Bloodied, Activation = Activation.BonusAction };
        var read = RoundTrip(Draft(ContentKind.Feat, toggle));

        var typed = Assert.IsType<ToggleEffect>(read.Effects[0]);
        Assert.Equal((ToggleAutoWhen.Bloodied, Activation.BonusAction), (typed.AutoWhen, typed.Activation));
        Assert.Null(typed.Extensions);
        Assert.Contains("\"autoWhen\":\"bloodied\"", JsonSerializer.Serialize(read, RulesJson.Compact), StringComparison.Ordinal);
        Assert.Contains("\"activation\":\"bonusAction\"", JsonSerializer.Serialize(read, RulesJson.Compact), StringComparison.Ordinal);
    }

    [Fact]
    public void Resource_activation_round_trips_in_a_v10_revision()
    {
        var read = RoundTrip(Draft(ContentKind.Feat, Pool() with { Activation = Activation.Reaction }));
        var typed = Assert.IsType<ResourceEffect>(read.Effects[0]);
        Assert.Equal(Activation.Reaction, typed.Activation);
        Assert.Null(typed.Extensions);
    }

    [Theory]
    [InlineData(GrantKind.DamageResistance, "damageResistance")]
    [InlineData(GrantKind.DamageImmunity, "damageImmunity")]
    [InlineData(GrantKind.DamageVulnerability, "damageVulnerability")]
    public void The_damage_grants_round_trip_in_a_v10_revision(GrantKind kind, string json)
    {
        var read = RoundTrip(Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = kind, DamageType = "Fire" }));
        var typed = Assert.IsType<GrantEffect>(read.Effects[0]);
        Assert.Equal((kind, "Fire"), (typed.Grant, typed.DamageType));
        Assert.Null(typed.Extensions);
        var written = JsonSerializer.Serialize(read, RulesJson.Compact);
        Assert.Contains($"\"grant\":\"{json}\"", written, StringComparison.Ordinal);
        Assert.Contains("\"damageType\":\"Fire\"", written, StringComparison.Ordinal); // stored as written
    }

    [Fact]
    public void An_item_effect_round_trips_with_and_without_recovery_and_regain()
    {
        foreach (var charged in new[]
        {
            new ItemEffect { Id = "i" },
            new ItemEffect { Id = "i", RequiresAttunement = false },
            ChargedItem(),
            ChargedItem(recovery: new ItemChargeRecovery { Period = RestPeriod.LongRest }),
            ChargedItem(7, new ItemChargeRecovery { Period = RestPeriod.ShortRest, Regain = "1d4+1" }),
        })
        {
            var written = JsonSerializer.Serialize(Draft(ContentKind.Item, charged), RulesJson.Compact);
            var read = RoundTrip(Draft(ContentKind.Item, charged));
            Assert.Equal(charged, Assert.IsType<ItemEffect>(read.Effects[0]));
            Assert.Equal(written, JsonSerializer.Serialize(read, RulesJson.Compact));
        }
        var json = JsonSerializer.Serialize(Draft(ContentKind.Item, ChargedItem(7, new ItemChargeRecovery { Period = RestPeriod.ShortRest, Regain = "1d4+1" })), RulesJson.Compact);
        Assert.Contains("\"type\":\"item\"", json, StringComparison.Ordinal);
        Assert.Contains("\"requiresAttunement\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"charges\":{\"max\":7,\"recovery\":{\"period\":\"shortRest\",\"regain\":\"1d4\\u002B1\"}}", json, StringComparison.Ordinal); // the serializer escapes +
    }

    [Fact]
    public void Nothing_new_is_written_when_the_v10_keys_are_absent()
    {
        var json = JsonSerializer.Serialize(Draft(ContentKind.Item, Toggle(), Pool(), new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = "skill.stealth" }), RulesJson.Compact);
        foreach (var key in new[] { "autoWhen", "activation", "damageType", "requiresAttunement", "charges" })
            Assert.DoesNotContain($"\"{key}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_effect_with_a_malformed_body_stays_reference_only_in_v10()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Draft(ContentKind.Item, ChargedItem()), RulesJson.Compact))!;
        node["effects"]![0]!["charges"] = JsonNode.Parse("{\"max\":\"three\"}");
        var read = JsonSerializer.Deserialize<ContentRevision>(node.ToJsonString(), RulesJson.Compact)!;
        Assert.IsType<UnknownEffect>(read.Effects[0]);
    }

    // ---- the shared damage-type mapper -----------------------------------------------------------------------------

    [Theory]
    [InlineData("fire", "fire")]
    [InlineData("Fire", "fire")]
    [InlineData("NECROTIC", "necrotic")]
    [InlineData("Bludgeoning", "bludgeoning")]
    public void TryNormalize_maps_case_insensitively_to_the_lowercase_name(string input, string expected)
    {
        Assert.True(DamageTypes.TryNormalize(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" fire")]
    [InlineData("fire ")]
    [InlineData("fires")]
    [InlineData("magic")]
    public void TryNormalize_refuses_anything_else(string? input)
    {
        Assert.False(DamageTypes.TryNormalize(input, out var canonical));
        Assert.Equal("", canonical);
    }

    [Fact]
    public void TryNormalize_accepts_every_listed_damage_type_and_only_those()
    {
        foreach (var type in DamageTypes.All)
        {
            Assert.True(DamageTypes.TryNormalize(type.ToUpperInvariant(), out var canonical));
            Assert.Equal(type, canonical);
        }
        Assert.Equal(13, DamageTypes.All.Count);
    }

    // ---- RequiredSchemaVersion --------------------------------------------------------------------------------------

    public static TheoryData<string> Additions =>
    [
        "passive modifier", "speed modifier", "passive restriction", "speed restriction", "toggle autoWhen", "toggle activation",
        "resource activation", "damage resistance", "damage immunity", "damage vulnerability", "item",
    ];

    private static ContentRevision Using(string addition) => addition switch
    {
        "passive modifier" => Draft(ContentKind.Feat, new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("insight"), Value = "1" }),
        "speed modifier" => Draft(ContentKind.Feat, new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" }),
        "passive restriction" => Draft(ContentKind.Feat, new RestrictionEffect { Id = "r", Field = FieldIds.Passive("perception"), Minimum = 10 }),
        "speed restriction" => Draft(ContentKind.Feat, new RestrictionEffect { Id = "r", Field = FieldIds.Speed, Minimum = 30 }),
        "toggle autoWhen" => Draft(ContentKind.Feat, Toggle() with { AutoWhen = ToggleAutoWhen.Bloodied }),
        "toggle activation" => Draft(ContentKind.Feat, Toggle() with { Activation = Activation.Action }),
        "resource activation" => Draft(ContentKind.Feat, Pool() with { Activation = Activation.Other }),
        "damage resistance" => Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = GrantKind.DamageResistance, DamageType = "cold" }),
        "damage immunity" => Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = GrantKind.DamageImmunity, DamageType = "poison" }),
        "damage vulnerability" => Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = GrantKind.DamageVulnerability, DamageType = "fire" }),
        "item" => Draft(ContentKind.Item, new ItemEffect { Id = "i", RequiresAttunement = true }),
        _ => throw new ArgumentOutOfRangeException(nameof(addition)),
    };

    [Theory]
    [MemberData(nameof(Additions))]
    public void Each_v10_addition_alone_makes_the_required_version_10(string addition)
    {
        var report = Validate(Using(addition));
        Assert.True(report.CanPublish, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Equal(10, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Content_that_uses_no_v10_addition_keeps_its_lower_required_version()
    {
        Assert.Equal(3, Validate(Draft(ContentKind.Feat, new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" })).RequiredSchemaVersion);
        Assert.Equal(6, Validate(Draft(ContentKind.Feat, Toggle())).RequiredSchemaVersion);
        Assert.Equal(3, Validate(Draft(ContentKind.Feat, Pool())).RequiredSchemaVersion);
        Assert.Equal(3, Validate(Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = "skill.stealth" })).RequiredSchemaVersion);
        Assert.Equal(9, Validate(Draft(ContentKind.Feat, new ChoiceEffect { Id = "c", ChoiceId = "c" })).RequiredSchemaVersion);
        Assert.Equal(8, Validate(Draft(ContentKind.Feat, new ModifierEffect { Id = "m", Operation = ModifierOperation.Set, Target = FieldIds.Attacks, Value = "2" })).RequiredSchemaVersion);
    }

    [Fact]
    public void A_v10_addition_beside_older_features_still_requires_10()
    {
        var report = Validate(Draft(ContentKind.Feat, Toggle(), new ModifierEffect { Id = "m", Operation = ModifierOperation.Set, Target = FieldIds.Attacks, Value = "2" }, Resist("fire")));
        Assert.Equal(10, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_typed_v10_use_in_a_revision_that_declares_an_older_version_is_named_as_needing_v10()
    {
        // JSON reads cannot produce these below v10 (the gate keeps the keys out), but a revision built in code can.
        foreach (var addition in new[] { "toggle autoWhen", "toggle activation", "resource activation", "damage resistance", "item" })
            Assert.Contains("validate.requires-v10", Errors(Using(addition) with { SchemaVersion = 9 }));
    }

    // ---- passive and speed targets in the validator ------------------------------------------------------------------

    [Fact]
    public void The_validator_refuses_passive_and_speed_targets_below_v10_and_accepts_them_in_v10()
    {
        foreach (var addition in new[] { "passive modifier", "speed modifier", "passive restriction", "speed restriction" })
        {
            var below = Validate(Using(addition) with { SchemaVersion = 9 });
            Assert.Contains(below.Errors, e => e.Code == "validate.unknown-target" && e.Message.Contains("cannot be targeted by content", StringComparison.Ordinal));
            Assert.False(below.CanPublish);
            Assert.DoesNotContain("validate.unknown-target", Errors(Using(addition)));
        }
    }

    // ---- damage grants ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("fire")]
    [InlineData("Fire")]
    [InlineData("THUNDER")]
    public void A_damage_grant_with_a_listed_damage_type_in_any_case_is_accepted(string damageType) =>
        Assert.Empty(Errors(Draft(ContentKind.Feat, Resist(damageType))));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("magic")]
    [InlineData(" fire")]
    public void A_damage_grant_needs_a_listed_damage_type(string? damageType) =>
        Assert.Contains("validate.grant-damage-type", Errors(Draft(ContentKind.Feat, Resist(damageType))));

    [Theory]
    [InlineData(GrantKind.DamageImmunity)]
    [InlineData(GrantKind.DamageVulnerability)]
    public void All_three_damage_grants_need_a_damage_type(GrantKind kind) =>
        Assert.Contains("validate.grant-damage-type", Errors(Draft(ContentKind.Feat, new GrantEffect { Id = "g", Grant = kind })));

    [Theory]
    [InlineData(GrantKind.Proficiency)]
    [InlineData(GrantKind.Expertise)]
    [InlineData(GrantKind.Content)]
    public void A_damage_type_is_refused_on_any_other_grant_kind(GrantKind kind)
    {
        var grant = new GrantEffect { Id = "g", Grant = kind, DamageType = "fire", Target = "skill.stealth", Content = kind == GrantKind.Content ? Fixtures.Watchful : null };
        Assert.Contains("validate.grant-damage-type-kind", Errors(Draft(ContentKind.Feat, grant)));
    }

    [Fact]
    public void A_target_or_content_is_refused_on_a_damage_grant_without_a_second_unknown_target_error()
    {
        var withTarget = Errors(Draft(ContentKind.Feat, Resist("fire") with { Target = "skill.stealth" })).ToList();
        Assert.Contains("validate.grant-damage-fields", withTarget);
        Assert.DoesNotContain("validate.unknown-target", withTarget);
        Assert.Contains("validate.grant-damage-fields", Errors(Draft(ContentKind.Feat, Resist("fire") with { Content = Fixtures.Watchful })));
        Assert.Empty(Errors(Draft(ContentKind.Feat, Resist("fire") with { Level = 3, OnlyAs = ClassEntry.StartingClass })));
    }

    [Fact]
    public void A_damage_grant_without_a_target_does_not_raise_unknown_target()
    {
        Assert.DoesNotContain("validate.unknown-target", Errors(Draft(ContentKind.Feat, Resist("fire"))));
    }

    // ---- item effect -----------------------------------------------------------------------------------------------

    [Fact]
    public void An_item_effect_is_valid_on_an_item_and_refused_on_any_other_kind()
    {
        Assert.Empty(Errors(Draft(ContentKind.Item, ChargedItem())));
        foreach (var kind in new[] { ContentKind.Feat, ContentKind.Feature, ContentKind.Class, ContentKind.Species, ContentKind.Spell })
            Assert.Contains("validate.item-kind", Errors(Draft(kind, ChargedItem())));
    }

    [Fact]
    public void The_item_kind_and_unsupported_effect_messages_quote_no_document_text()
    {
        var wrongKind = Validate(Draft(ContentKind.Feat, ChargedItem())).Errors.Single(e => e.Code == "validate.item-kind");
        Assert.DoesNotContain("Fixture V10", wrongKind.Message, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(Draft(ContentKind.Feat, ChargedItem()), RulesJson.Compact).Replace("\"type\":\"item\"", "\"type\":\"zzmarkerzz\"", StringComparison.Ordinal);
        var unknown = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!;
        Assert.IsType<UnknownEffect>(unknown.Effects[0]);
        var warning = Validate(unknown).Warnings.Single(w => w.Code == "validate.effect-unsupported");
        Assert.DoesNotContain("zzmarkerzz", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void At_most_one_item_effect_is_allowed_per_revision()
    {
        Assert.Contains("validate.item-duplicate", Errors(Draft(ContentKind.Item, new ItemEffect { Id = "a" }, new ItemEffect { Id = "b" })));
        Assert.DoesNotContain("validate.item-duplicate", Errors(Draft(ContentKind.Item, new ItemEffect { Id = "a" })));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, true)]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(-1, false)]
    public void Charges_max_is_1_to_99(int max, bool valid)
    {
        var errors = Errors(Draft(ContentKind.Item, ChargedItem(max)));
        Assert.Equal(valid, !errors.Contains("validate.item-charges"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1d4", true)]
    [InlineData("1d6+1", true)]
    [InlineData("2d4+2", true)]
    [InlineData("bogus", false)]
    [InlineData("1d", false)]
    [InlineData("", false)]
    [InlineData("1d6+", false)]
    public void A_regain_must_parse_as_dice(string? regain, bool valid)
    {
        var item = ChargedItem(5, new ItemChargeRecovery { Period = RestPeriod.LongRest, Regain = regain });
        var report = Validate(Draft(ContentKind.Item, item));
        Assert.Equal(valid, report.Errors.All(e => e.Code != "validate.dice-invalid"));
        Assert.Equal(valid, report.CanPublish);
    }

    [Fact]
    public void An_error_about_a_regain_does_not_quote_the_document_text()
    {
        var item = ChargedItem(5, new ItemChargeRecovery { Period = RestPeriod.LongRest, Regain = "SECRET-TEXT-9" });
        var error = Validate(Draft(ContentKind.Item, item)).Errors.Single(e => e.Code == "validate.dice-invalid");
        Assert.DoesNotContain("SECRET-TEXT-9", error.Message, StringComparison.Ordinal);
    }

    // ---- calculation: no behaviour beyond the targets ---------------------------------------------------------------

    [Fact]
    public void A_v10_damage_grant_an_item_effect_and_the_new_toggle_keys_change_nothing_on_the_sheet()
    {
        var pack = Fixtures.Pack();
        var character = Fixtures.Srd51Character();
        string SheetOf(ContentRevision? extra)
        {
            var revisions = extra is null ? pack.Revisions : [.. pack.Revisions, extra];
            var pins = extra is null ? character.Pins : [.. character.Pins, extra.Reference];
            return JsonSerializer.Serialize(CharacterCalculator.Calculate(character with { Pins = pins }, new InMemoryContentCatalog(pack.Sources, revisions)), RulesJson.Options);
        }
        var baseline = SheetOf(null);
        var feature = Draft(ContentKind.Feat, Resist("fire"), Toggle() with { AutoWhen = ToggleAutoWhen.Bloodied, Activation = Activation.Reaction }, Pool() with { Activation = Activation.Other })
            with { Status = RevisionStatus.Published, RulesFamilies = [RulesFamilies.Srd51] };
        var item = Draft(ContentKind.Item, ChargedItem()) with { Status = RevisionStatus.Published, RulesFamilies = [RulesFamilies.Srd51], ContentId = Guid.Parse("c10a0000-0000-4000-8000-000000000003"), RevisionId = Guid.Parse("c10b0000-0000-4000-8000-000000000003") };

        var withFeature = JsonNode.Parse(SheetOf(feature))!;
        var diagnostics = withFeature["diagnostics"]?.AsArray().Select(d => (string?)d!["code"]).ToList() ?? [];
        Assert.DoesNotContain("effect.unknown-target", diagnostics);
        Assert.Equal(JsonNode.Parse(baseline)!["fields"]!.ToJsonString(), withFeature["fields"]!.ToJsonString());
        Assert.Equal(JsonNode.Parse(baseline)!["fields"]!.ToJsonString(), JsonNode.Parse(SheetOf(item))!["fields"]!.ToJsonString());
    }
}
