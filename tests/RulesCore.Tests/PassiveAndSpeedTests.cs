namespace TomeStack.RulesCore.Tests;

/// <summary>D24 (owner, 2026-10-06): passive scores and a speed field as calculated data.</summary>
public class PassiveAndSpeedTests
{
    [Fact]
    public void Passive_scores_are_ten_plus_the_skill_with_a_trace_that_says_so()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        foreach (var key in new[] { "perception", "insight", "investigation" })
        {
            var passive = sheet.Field(FieldIds.Passive(key));
            Assert.Equal(10 + sheet.Field(FieldIds.Skill(key)).Value, passive.Value);
            Assert.Equal("score", passive.Units);
            Assert.StartsWith("Passive ", passive.Label);
            Assert.Contains(passive.Trace, s => s.Operation == "base" && s.Description.Contains("10 +"));
        }
    }

    private static ContentRevision Feat(int n, string name, int schemaVersion, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"a1000000-0000-4000-8000-00000000010{n}"), RevisionId = Guid.Parse($"b1000000-0000-4000-8000-00000000010{n}"),
        Kind = ContentKind.Feat, Name = name, RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared, new PageRef(31)),
        Status = RevisionStatus.Published, SchemaVersion = schemaVersion, Effects = effects,
    };

    private static CharacterSheet Calc(Character character, params ContentRevision[] extra)
    {
        var pack = Fixtures.Pack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, .. extra]);
        return CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(e => e.Reference)] }, catalog);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void The_calculator_ignores_a_content_modifier_on_speed_or_a_passive_whatever_the_schema_version(int schemaVersion)
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var quick = Feat(1, "Fixture Quickness", schemaVersion,
            new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" },
            new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" });
        var sheet = Calc(Fixtures.Srd51Character(), quick);
        Assert.Equal(30, sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(plain.Field(FieldIds.Passive("perception")).Value, sheet.Field(FieldIds.Passive("perception")).Value);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.Content == quick.Reference && d.EffectId == "fast");
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.EffectId == "pp");
    }

    [Fact]
    public void The_calculator_ignores_a_restriction_on_a_passive_and_does_not_apply_the_content()
    {
        var gated = Feat(2, "Fixture Gated Watchfulness", 2,
            new RestrictionEffect { Id = "needs-pp", Field = FieldIds.Passive("perception"), Minimum = 1 },
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" });
        var sheet = Calc(Fixtures.Srd51Character(), gated);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.Content == gated.Reference && d.EffectId == "needs-pp");
        Assert.DoesNotContain(gated.Reference, sheet.Active!);
    }

    // ---- content schema v10 (Wave 3, D42): a v10 revision may target speed and the passive scores ------------------------

    [Fact]
    public void A_v10_modifier_on_speed_and_on_a_passive_applies_and_is_traced()
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var quick = Feat(4, "Fixture Quickness", 10,
            new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" },
            new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" },
            new ModifierEffect { Id = "pi", Operation = ModifierOperation.Set, Target = FieldIds.Passive("insight"), Value = "25" });
        var sheet = Calc(Fixtures.Srd51Character(), quick);

        Assert.Equal(40, sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(plain.Field(FieldIds.Passive("perception")).Value + 5, sheet.Field(FieldIds.Passive("perception")).Value);
        Assert.Equal(25, sheet.Field(FieldIds.Passive("insight")).Value);
        Assert.Contains(sheet.Field(FieldIds.Speed).Trace, s => s.Origin.Kind == TraceOriginKind.Content && s.Amount == 10);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "effect.character-only-field");
        Assert.Equal(plain.Field(FieldIds.Passive("investigation")).Value, sheet.Field(FieldIds.Passive("investigation")).Value);
    }

    [Fact]
    public void A_v10_passive_modifier_sees_a_skill_changed_by_other_content()
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var keen = Feat(5, "Fixture Keen", 10,
            new ModifierEffect { Id = "skill", Operation = ModifierOperation.Bonus, Target = FieldIds.Skill("perception"), Value = "2" },
            new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "3" });
        var sheet = Calc(Fixtures.Srd51Character(), keen);
        Assert.Equal(plain.Field(FieldIds.Passive("perception")).Value + 5, sheet.Field(FieldIds.Passive("perception")).Value);
    }

    [Fact]
    public void A_v10_restriction_on_speed_or_a_passive_is_checked_like_any_other_field()
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var met = Feat(6, "Fixture Met", 10,
            new RestrictionEffect { Id = "needs-speed", Field = FieldIds.Speed, Minimum = 30 },
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" });
        var metSheet = Calc(Fixtures.Srd51Character(), met);
        Assert.Contains(met.Reference, metSheet.Active!);
        Assert.Equal(plain.Field(FieldIds.Initiative).Value + 5, metSheet.Field(FieldIds.Initiative).Value);
        Assert.DoesNotContain(metSheet.Diagnostics, d => d.Code is "effect.character-only-field" or "restriction.unmet");

        var unmet = Feat(7, "Fixture Unmet", 10,
            new RestrictionEffect { Id = "needs-pp", Field = FieldIds.Passive("perception"), Minimum = 99 },
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" });
        var unmetSheet = Calc(Fixtures.Srd51Character(), unmet);
        Assert.Contains(unmetSheet.Diagnostics, d => d.Code == "restriction.unmet" && d.Content == unmet.Reference);
        Assert.DoesNotContain(unmetSheet.Diagnostics, d => d.Code == "effect.character-only-field");
        Assert.DoesNotContain(unmet.Reference, unmetSheet.Active!);
    }

    [Fact]
    public void The_stats_override_on_speed_and_a_passive_stays_final_beside_v10_content()
    {
        var quick = Feat(8, "Fixture Quickness", 10,
            new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" },
            new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" });
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var sheet = Calc(Fixtures.Srd51Character() with { Overrides = [new(FieldIds.Speed, 25, "Fixture reason"), new(FieldIds.Passive("perception"), 12, "Fixture reason")] }, quick);
        Assert.Equal((25, 40), (sheet.Field(FieldIds.Speed).Value, sheet.Field(FieldIds.Speed).ComputedValue));
        Assert.Equal((12, plain.Field(FieldIds.Passive("perception")).Value + 5), (sheet.Field(FieldIds.Passive("perception")).Value, sheet.Field(FieldIds.Passive("perception")).ComputedValue));
    }

    [Fact]
    public void A_v10_revision_stored_with_a_speed_target_survives_a_json_round_trip_and_still_applies()
    {
        var quick = Feat(9, "Fixture Quickness", 10, new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" });
        var stored = System.Text.Json.JsonSerializer.Serialize(quick, RulesJson.Compact);
        var read = System.Text.Json.JsonSerializer.Deserialize<ContentRevision>(stored, RulesJson.Compact)!;
        Assert.Equal(10, read.SchemaVersion);
        Assert.Equal(40, Calc(Fixtures.Srd51Character(), read).Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void A_character_override_on_speed_still_applies_beside_content_that_targets_it()
    {
        var quick = Feat(3, "Fixture Quickness", 2, new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" });
        var sheet = Calc(Fixtures.Srd51Character() with { Overrides = [new(FieldIds.Speed, 25, "Fixture reason")] }, quick);
        Assert.Equal((25, 30), (sheet.Field(FieldIds.Speed).Value, sheet.Field(FieldIds.Speed).ComputedValue));
    }

    [Fact]
    public void Speed_starts_at_thirty_feet_by_rules_policy_and_can_be_overridden()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var speed = sheet.Field(FieldIds.Speed);
        Assert.Equal((30, "feet"), (speed.Value, speed.Units));
        Assert.Equal(TraceOriginKind.RulesPolicy, speed.Trace[0].Origin.Kind);
        var dwarf = CharacterCalculator.Calculate(Fixtures.Srd51Character() with { Overrides = [new(FieldIds.Speed, 25, "Fixture reason")] }, Fixtures.Catalog());
        Assert.Equal((25, 30), (dwarf.Field(FieldIds.Speed).Value, dwarf.Field(FieldIds.Speed).ComputedValue));
    }

    // ---- Task 8 (items 12, 24): a species sets its speed with a v10 replace; a feat's bonus still adds on top --------------

    private static ContentRevision Source(int n, ContentKind kind, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"a1000000-0000-4000-8000-00000000020{n}"), RevisionId = Guid.Parse($"b1000000-0000-4000-8000-00000000020{n}"),
        Kind = kind, Name = $"Fixture Speed Source {n}", RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Fixtures.SourceShared, new PageRef(32)), Status = RevisionStatus.Published, SchemaVersion = 10, Effects = effects,
    };

    private static ModifierEffect SpeedEffect(string id, ModifierOperation operation, string value) => new() { Id = id, Operation = operation, Target = FieldIds.Speed, Value = value };

    [Theory]
    [InlineData("srd-5.1")]
    [InlineData("srd-5.2.1")]
    public void A_species_speed_replace_sets_the_base_and_a_bonus_from_other_content_adds_on_top(string family)
    {
        var character = family == RulesFamilies.Srd51 ? Fixtures.Srd51Character() : Fixtures.Srd521Character();
        var folk = Source(1, ContentKind.Species, SpeedEffect("speed", ModifierOperation.Replace, "25"));
        var quick = Source(2, ContentKind.Feat, SpeedEffect("fast", ModifierOperation.Bonus, "10"));

        Assert.Equal(30, CharacterCalculator.Calculate(character, Fixtures.Catalog()).Field(FieldIds.Speed).Value);
        var alone = Calc(character, folk).Field(FieldIds.Speed);
        Assert.Equal(25, alone.Value);
        Assert.Contains(alone.Trace, s => s.Origin.Kind == TraceOriginKind.Content && s.Operation == "replace");
        Assert.Equal(35, Calc(character, folk, quick).Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void Of_several_speed_replaces_the_highest_wins()
    {
        var slow = Source(3, ContentKind.Species, SpeedEffect("speed", ModifierOperation.Replace, "25"));
        var fast = Source(4, ContentKind.Feat, SpeedEffect("speed", ModifierOperation.Replace, "40"));
        Assert.Equal(40, Calc(Fixtures.Srd51Character(), slow, fast).Field(FieldIds.Speed).Value);
    }

    [Fact]
    public void A_granted_feature_names_what_granted_it()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("srd521-ash-m1.json"), Fixtures.M1Catalog());
        var granted = sheet.Features!.First(f => f.GrantedBy is not null);
        Assert.False(string.IsNullOrEmpty(granted.GrantedByName));
        Assert.Contains(sheet.Features!, f => f.Content == granted.GrantedBy); // the granter is itself on the sheet
    }
}
