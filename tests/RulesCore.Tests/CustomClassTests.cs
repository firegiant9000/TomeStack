using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M5 slice 1 (ADR-010, content schema v9): a nonstandard class levels 1–20 and multiclasses without code edits. The
/// original "Test Chronicler" (<c>fixture-pack-m5-chronicler.json</c>) has a d8, two columns (<c>scale</c>), a resource,
/// a recovery, a roll bonus, a skill modifier and a prepared-spell formula that read them (<c>SCALE.&lt;id&gt;</c>), its
/// own slot table, a two-thirds multiclass share by table, a granted feature and a subclass with a column of its own.
/// Every test runs under both rules families side by side.
/// </summary>
public class CustomClassTests
{
    private static readonly string[] BothFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static Character Chronicler(string family, int intelligence = 16, params ClassLevel[] classes) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            BaseAbilities = new(10, 12, 14, intelligence, 14, 14),
            Spells = [],
            Choices = [new(Fixtures.Chronicler, "chronicler-archive", [Fixtures.ArchiveOfEchoes])],
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.ChroniclerCatalog());

    private static int[] Slots(CharacterSheet sheet) => [.. Enumerable.Range(1, 9).Select(l => sheet.Field(FieldIds.SpellSlots(l)).Value)];

    private static ContentRevision ChroniclerRevision() => Fixtures.ChroniclerPack().Revisions.Single(r => r.Reference == Fixtures.Chronicler);

    [Theory]
    // level, ink, lore, echo (0 before the subclass at 3), initiative, spells prepared, own slots at that level
    [InlineData(1, 2, 1, 0, 1, 4, new[] { 1 })]
    [InlineData(3, 3, 1, 1, 1, 4, new[] { 3 })]
    [InlineData(5, 4, 2, 1, 2, 5, new[] { 3, 2 })]
    [InlineData(11, 6, 3, 2, 2, 6, new[] { 4, 3, 3, 1 })]
    [InlineData(17, 8, 4, 3, 3, 7, new[] { 4, 3, 3, 3, 2, 1 })]
    [InlineData(20, 9, 4, 4, 3, 7, new[] { 4, 3, 3, 3, 3, 2, 1 })]
    public void The_Test_Chronicler_levels_1_to_20_from_its_columns_side_by_side(int level, int ink, int lore, int echo, int initiative, int prepared, int[] ownSlots)
    {
        foreach (var family in BothFamilies)
        {
            var sheet = Sheet(Chronicler(family, classes: new ClassLevel(Fixtures.Chronicler, level)));

            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "effect.invalid-formula" or "scale.duplicate" or "scale.invalid" or "content.schema-unsupported");
            // d8: 8 at level 1, then 5 per level, plus Con +2 per level.
            Assert.Equal(8 + (5 * (level - 1)) + (2 * level), sheet.Field(FieldIds.HitPoints).Value);

            var resource = sheet.Resources!.Single(r => r.ResourceId == "ink");
            Assert.Equal((ink, AutomationStatus.Automatic), (resource.Maximum, resource.Automation));
            Assert.Contains(resource.Trace.Single().Inputs!, i => i.Name == "SCALE.ink" && i.Value == ink);
            Assert.Equal(ink / 2, resource.Recoveries.Single(r => r.On == RestPeriod.ShortRest).Value);
            Assert.True(resource.Recoveries.Single(r => r.On == RestPeriod.LongRest).All);

            var inkblot = sheet.Features!.Single(f => f.Content == Fixtures.Chronicler).Effects.Single(e => e.Id == "chronicler-inkblot");
            Assert.Equal((lore, 1), (inkblot.Bonus, inkblot.Cost));

            Assert.Equal(3 + lore, sheet.Field("skill.history").Value); // Int +3, plus the Lore column
            Assert.Equal(initiative, sheet.Field(FieldIds.Initiative).Value); // Dex +1, plus the granted feature's floor(ink / 4) from level 2
            Assert.Equal(prepared, sheet.Spellcasting!.Single().SpellsAllowed); // max(1, INT.MOD + SCALE.lore)
            Assert.Equal([.. ownSlots, .. Enumerable.Repeat(0, 9 - ownSlots.Length)], Slots(sheet)); // a single caster keeps its own table

            var columns = sheet.Scales!.ToDictionary(s => s.ScaleId, s => s.Value);
            Assert.Equal(ink, columns["ink"]);
            Assert.Equal(lore, columns["lore"]);
            if (level >= 3)
            {
                Assert.Equal(echo, columns["echo"]); // the subclass's own column, indexed by the class level
                Assert.Equal(3 + echo + lore, sheet.Field("skill.arcana").Value);
            }
            else
            {
                Assert.False(columns.ContainsKey("echo")); // the subclass does not apply before level 3
            }
            Assert.All(sheet.Scales!, s => Assert.Equal((Fixtures.Chronicler, level), (s.Class, s.ClassLevel)));
        }
    }

    [Fact]
    public void A_table_caster_combines_with_a_full_caster_exactly_under_both_families()
    {
        foreach (var family in BothFamilies)
        {
            // Chronicler 5 counts 3 (its table), the full caster 3: caster level 6 on the Multiclass Spellcaster table.
            var sheet = Sheet(Chronicler(family, classes: [new(Fixtures.Chronicler, 5), new(Fixtures.Loremaster, 3)]));

            Assert.Equal([4, 3, 3, 0, 0, 0, 0, 0, 0], Slots(sheet));
            var field = sheet.Field(FieldIds.SpellSlots(1));
            Assert.Equal(AutomationStatus.Automatic, field.Automation);
            Assert.DoesNotContain(field.Warnings, w => w.Code == "spellcasting.multiclass-slots");
            var steps = field.Trace.Where(t => t.Field == FieldIds.SpellSlots(1)).ToList();
            Assert.Contains(steps, s => s.Origin.Content == Fixtures.Chronicler && s.Amount == 3 && s.Description.Contains("its multiclass table", StringComparison.Ordinal));
            Assert.Equal(6, steps[^1].Amount);
        }
    }

    [Fact]
    public void A_table_caster_with_a_half_caster_differs_only_by_the_half_casters_family_rounding()
    {
        ClassLevel[] classes = [new(Fixtures.Chronicler, 5), new(Fixtures.Wayfinder, 5)];

        // 3 (the table, no rounding) + floor(5 / 2) = 5 under 2014 rules; 3 + ceil(5 / 2) = 6 under 2024 rules.
        Assert.Equal([4, 3, 2, 0, 0, 0, 0, 0, 0], Slots(Sheet(Chronicler(RulesFamilies.Srd51, classes: classes))));
        Assert.Equal([4, 3, 3, 0, 0, 0, 0, 0, 0], Slots(Sheet(Chronicler(RulesFamilies.Srd521, classes: classes))));
    }

    [Fact]
    public void A_table_caster_with_a_third_caster_and_with_a_non_caster_side_by_side()
    {
        foreach (var family in BothFamilies)
        {
            // 4 (Chronicler 7) + floor(3 / 3) = caster level 5.
            Assert.Equal([4, 3, 2, 0, 0, 0, 0, 0, 0], Slots(Sheet(Chronicler(family, classes: [new(Fixtures.Chronicler, 7), new(Fixtures.Runeblade, 3)]))));

            // With a class that casts nothing, the Chronicler is the only slot caster and keeps its own table at level 3.
            var withScholar = Sheet(Chronicler(family, classes: [new(Fixtures.Chronicler, 3), new(Fixtures.Scholar, 2)]));
            Assert.Equal([3, 0, 0, 0, 0, 0, 0, 0, 0], Slots(withScholar));
            Assert.Equal(3, withScholar.Resources!.Single(r => r.ResourceId == "ink").Maximum); // its class level, not the total
            Assert.DoesNotContain(withScholar.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
        }
    }

    [Fact]
    public void The_multiclass_prerequisite_and_the_starting_class_saves_come_from_content_side_by_side()
    {
        foreach (var family in BothFamilies)
        {
            var weak = Sheet(Chronicler(family, intelligence: 12, classes: [new(Fixtures.Scholar, 2), new(Fixtures.Chronicler, 1)]));
            Assert.Contains(weak.Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == Fixtures.Chronicler);

            // Saving throws only as the starting class (onlyAs). Wisdom: the Scholar grants an Intelligence save of its own.
            var starting = Sheet(Chronicler(family, classes: [new(Fixtures.Chronicler, 1), new(Fixtures.Scholar, 1)]));
            var later = Sheet(Chronicler(family, classes: [new(Fixtures.Scholar, 1), new(Fixtures.Chronicler, 1)]));
            Assert.Equal(2 + 2, starting.Field("save.wis").Value); // Wis +2, PB +2
            Assert.Equal(2, later.Field("save.wis").Value);
        }
    }

    [Fact]
    public void SCALE_outside_a_class_is_unavailable_and_the_rest_calculates()
    {
        foreach (var family in BothFamilies)
        {
            // The feature pinned on its own belongs to no class, so its SCALE.ink is unavailable.
            var character = Chronicler(family, classes: []) with { Pins = [Fixtures.ChroniclerMarginalia], Choices = [] };
            var sheet = Sheet(character);
            Assert.Contains(sheet.Field(FieldIds.Initiative).Warnings, w => w.Code == "effect.invalid-formula" && w.Message.Contains("formula.value-unavailable", StringComparison.Ordinal));
            Assert.Equal(1, sheet.Field(FieldIds.Initiative).Value);
        }
    }

    [Fact]
    public void A_scale_reads_no_field_and_adds_no_dependency_edge()
    {
        Assert.True(Formula.TryParse("SCALE.ink + PB", allowScales: true, out var formula, out _));
        Assert.Null(FormulaIdentifiers.FieldFor("SCALE.ink"));
        Assert.Equal([FieldIds.ProficiencyBonus], formula!.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>());
        Assert.Empty(CharacterCalculator.DependencyCycles(ChroniclerRevision()));
    }

    [Theory]
    [InlineData("SCALE.ink", true)]
    [InlineData("SCALE.inkWell2", true)]
    [InlineData("SCALE.Ink", false)]
    [InlineData("SCALE.2ink", false)]
    [InlineData("SCALE.", false)]
    [InlineData("SCALE.ink_well", false)]
    [InlineData("SCALE.abcdefghijklmnopqrstuvwxyzabcdefg", false)] // 33 characters
    public void Scale_identifiers_follow_the_id_pattern(string identifier, bool valid)
    {
        Assert.Equal(valid, Formula.TryParse(identifier, allowScales: true, out _, out _));
    }

    [Fact]
    public void SCALE_in_a_v8_revision_is_an_unknown_identifier_as_in_builds_before_v9()
    {
        Assert.False(Formula.TryParse("SCALE.ink", out _, out var error));
        Assert.Equal("formula.unknown-identifier", error!.Code);

        foreach (var family in BothFamilies)
        {
            var v8 = ChroniclerRevision() with { SchemaVersion = 8 };
            var catalog = new InMemoryContentCatalog(
                [.. Fixtures.ChroniclerPack().Sources, .. Fixtures.MulticlassPack().Sources],
                [v8, .. Fixtures.ChroniclerPack().Revisions.Where(r => r.Reference != Fixtures.Chronicler)]);
            var sheet = CharacterCalculator.Calculate(Chronicler(family, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);

            var ink = sheet.Resources!.Single(r => r.ResourceId == "ink");
            Assert.Null(ink.Maximum);
            Assert.Contains(ink.Warnings, w => w.Message.Contains("formula.unknown-identifier", StringComparison.Ordinal));
            // The class's own columns are not read from a revision relabelled v8; its v9 subclass keeps its own.
            Assert.Equal(["echo"], sheet.Scales!.Select(s => s.ScaleId));
        }
    }

    /// <summary>
    /// The Chronicler as a v8 build would have stored it: <c>schemaVersion</c> 8, and <c>multiclassCasterTable</c> (which a
    /// v8 build keeps as extension data) written last in its effect, where extension data goes.
    /// </summary>
    private static string AsStoredByAV8Build()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(ChroniclerRevision(), RulesJson.Options))!;
        node["schemaVersion"] = 8;
        var spellcasting = node["effects"]!.AsArray().Single(e => (string?)e!["type"] == "spellcasting")!.AsObject();
        var table = spellcasting["multiclassCasterTable"]!.DeepClone();
        spellcasting.Remove("multiclassCasterTable");
        spellcasting.Add("multiclassCasterTable", table);
        return node.ToJsonString(RulesJson.Options);
    }

    [Fact]
    public void A_scale_effect_in_a_revision_older_than_v9_stays_unknown_and_byte_for_byte()
    {
        var chronicler = ChroniclerRevision();
        Assert.IsType<ScaleEffect>(chronicler.Effects[1]);

        var v8 = AsStoredByAV8Build();
        var old = JsonSerializer.Deserialize<ContentRevision>(v8, RulesJson.Options)!;

        Assert.IsType<UnknownEffect>(old.Effects[1]);
        Assert.Equal(v8, JsonSerializer.Serialize(old, RulesJson.Options));
    }

    [Fact]
    public void A_v8_spellcasting_revision_with_the_table_as_extension_data_is_unchanged_and_not_combined()
    {
        // As a v8 build reads it: the key is extension data, so it is not typed, not combined, and written back as it was.
        var v8 = AsStoredByAV8Build();
        var old = JsonSerializer.Deserialize<ContentRevision>(v8, RulesJson.Options)!;
        var spellcasting = Assert.IsType<SpellcastingEffect>(old.Effects.Single(e => e.Type == SpellcastingEffect.TypeName));

        Assert.Null(spellcasting.MulticlassCasterTable);
        Assert.True(spellcasting.Extensions!.ContainsKey("multiclassCasterTable"));
        Assert.Equal(v8, JsonSerializer.Serialize(old, RulesJson.Options));

        // A later extension key keeps its place too.
        var node = JsonNode.Parse(v8)!;
        var effect = node["effects"]!.AsArray().Single(e => (string?)e!["type"] == "spellcasting")!.AsObject();
        effect.Add("zzTrailing", 1);
        var withTrailing = node.ToJsonString(RulesJson.Options);
        Assert.Equal(withTrailing, JsonSerializer.Serialize(JsonSerializer.Deserialize<ContentRevision>(withTrailing, RulesJson.Options), RulesJson.Options));

        var catalog = new InMemoryContentCatalog(
            [.. Fixtures.ChroniclerPack().Sources, .. Fixtures.MulticlassPack().Sources],
            [old, .. Fixtures.MulticlassPack().Revisions]);
        var sheet = CharacterCalculator.Calculate(Chronicler(RulesFamilies.Srd521, classes: [new(Fixtures.Chronicler, 5), new(Fixtures.Loremaster, 3)]), catalog);
        Assert.Contains(sheet.Field(FieldIds.SpellSlots(1)).Warnings, w => w.Code == "spellcasting.multiclass-slots");
    }

    /// <summary>The v8-stored Chronicler with the table key replaced by <paramref name="name"/> and <paramref name="value"/>.</summary>
    private static string V8WithTableKey(string name, JsonNode? value)
    {
        var node = JsonNode.Parse(AsStoredByAV8Build())!;
        var spellcasting = node["effects"]!.AsArray().Single(e => (string?)e!["type"] == "spellcasting")!.AsObject();
        spellcasting.Remove("multiclassCasterTable");
        spellcasting.Add(name, value);
        return node.ToJsonString(RulesJson.Options);
    }

    [Theory]
    [InlineData("MulticlassCasterTable", "[0,1,2,2,3,4,4,5,6,6,7,8,8,9,10,10,11,12,12,13]")] // another spelling: the serializer is case-insensitive
    [InlineData("multiclassCasterTable", "null")]
    [InlineData("multiclassCasterTable", "\"2/3\"")] // not a list at all
    [InlineData("multiclassCasterTable", "[1.5]")]
    public void Below_v9_any_spelling_and_value_of_the_table_key_stays_extension_data_byte_for_byte(string name, string value)
    {
        // Review fix (M5 1a): a v8 build never binds this key, whatever its value, so its spellcasting still types and
        // calculates, and the revision writes back exactly as stored.
        var json = V8WithTableKey(name, JsonNode.Parse(value));
        var revision = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Options)!;

        var spellcasting = Assert.IsType<SpellcastingEffect>(revision.Effects.Single(e => e.Type == SpellcastingEffect.TypeName));
        Assert.Null(spellcasting.MulticlassCasterTable);
        Assert.True(spellcasting.Extensions!.ContainsKey(name));
        Assert.Equal(json, JsonSerializer.Serialize(revision, RulesJson.Options));
        // The validator names it as needing v9 rather than letting it publish as v8 and be ignored.
        Assert.Contains(ContentValidator.Validate(revision, Fixtures.ChroniclerCatalog()).Errors, e => e.Code == "validate.requires-v9");
    }

    [Fact]
    public void A_scale_in_a_v8_draft_read_from_json_is_named_as_needing_v9()
    {
        var revision = JsonSerializer.Deserialize<ContentRevision>(AsStoredByAV8Build(), RulesJson.Options)!;
        Assert.IsType<UnknownEffect>(revision.Effects[1]);

        var report = ContentValidator.Validate(revision, Fixtures.ChroniclerCatalog());
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v9");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.effect-unsupported" && w.EffectId == "chronicler-ink-column");
    }

    [Fact]
    public void A_stored_scale_with_values_out_of_bounds_is_isolated_at_calculation()
    {
        var chronicler = ChroniclerRevision();
        var hostile = chronicler with
        {
            Effects = [.. chronicler.Effects.Select(e => e is ScaleEffect { ScaleId: "ink" } s ? s with { Values = [.. Enumerable.Repeat(int.MaxValue, 20)] } : e)],
        };
        var pack = Fixtures.ChroniclerPack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [hostile, .. pack.Revisions.Skip(1)]);

        var sheet = CharacterCalculator.Calculate(Chronicler(RulesFamilies.Srd521, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "scale.invalid" && d.EffectId == "chronicler-ink-column");
        Assert.DoesNotContain(sheet.Scales!, s => s.ScaleId == "ink");
        Assert.Null(sheet.Resources!.Single(r => r.ResourceId == "ink").Maximum);
    }

    [Fact]
    public void A_subclass_is_checked_against_its_classs_newest_published_revision_only()
    {
        var pack = Fixtures.ChroniclerPack();
        var chronicler = ChroniclerRevision();
        var subclass = pack.Revisions.Single(r => r.Reference == Fixtures.ArchiveOfEchoes) with
        {
            RevisionId = Guid.NewGuid(),
            Status = RevisionStatus.Draft,
            ExtendsChoice = new(chronicler.ContentId, "chronicler-archive"),
            Effects = [new ScaleEffect { Id = "s", ScaleId = "quill", Label = "Quill", Values = [.. Enumerable.Repeat(1, 20)] }],
        };
        Assert.DoesNotContain(ContentValidator.Validate(subclass, Fixtures.ChroniclerCatalog()).Errors, e => e.Code == "validate.scale-duplicate");

        // A class draft that defines "quill" never blocks it; an older published revision that defines it only warns.
        ContentRevision WithQuill(RevisionStatus status) => chronicler with
        {
            RevisionId = Guid.NewGuid(), Status = status,
            Effects = [.. chronicler.Effects, new ScaleEffect { Id = "q", ScaleId = "quill", Label = "Quill", Values = [.. Enumerable.Repeat(2, 20)] }],
        };
        var withDraft = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, WithQuill(RevisionStatus.Draft)]);
        Assert.DoesNotContain(ContentValidator.Validate(subclass, withDraft).Errors, e => e.Code == "validate.scale-duplicate");

        var older = WithQuill(RevisionStatus.Published);
        var olderThenNewer = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions.Skip(1), older, chronicler with { RevisionId = Guid.NewGuid() }]);
        var report = ContentValidator.Validate(subclass, olderThenNewer);
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.scale-duplicate");
        Assert.Contains(report.Warnings, w => w.Code == "validate.scale-duplicate-older");

        var newest = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, older]);
        Assert.Contains(ContentValidator.Validate(subclass, newest).Errors, e => e.Code == "validate.scale-duplicate");
    }

    [Fact]
    public void A_subclass_that_extends_a_features_choice_gets_no_class_scale_checks()
    {
        var pack = Fixtures.ChroniclerPack();
        var marginalia = pack.Revisions.Single(r => r.Reference == Fixtures.ChroniclerMarginalia);
        var feature = marginalia with
        {
            RevisionId = Guid.NewGuid(),
            Effects = [new ChoiceEffect { Id = "c", ChoiceId = "margin", Count = 1, Options = [Fixtures.ArchiveOfEchoes] }],
        };
        var subclass = pack.Revisions.Single(r => r.Reference == Fixtures.ArchiveOfEchoes) with
        {
            RevisionId = Guid.NewGuid(),
            Status = RevisionStatus.Draft,
            ExtendsChoice = new(feature.ContentId, "margin"),
        };
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, feature]);

        // It reads SCALE.lore from the class, which validation cannot see through a feature: no false warning.
        Assert.DoesNotContain(ContentValidator.Validate(subclass, catalog).Warnings, w => w.Code == "validate.scale-unknown");
    }

    [Fact]
    public void A_spellcasting_revision_without_the_table_serializes_unchanged()
    {
        var loremaster = Fixtures.MulticlassPack().Revisions.Single(r => r.Reference == Fixtures.Loremaster);
        Assert.DoesNotContain("multiclassCasterTable", JsonSerializer.Serialize(loremaster, RulesJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void The_Chronicler_validates_and_publishes_as_v9()
    {
        var catalog = Fixtures.ChroniclerCatalog();
        foreach (var revision in Fixtures.ChroniclerPack().Revisions)
        {
            var report = ContentValidator.Validate(revision, catalog);
            Assert.Empty(report.Errors);
            Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.scale-unknown");
            Assert.Equal(9, report.RequiredSchemaVersion);
        }
    }

    [Theory]
    [InlineData("modifier")]
    [InlineData("resource")]
    [InlineData("recovery")]
    [InlineData("cost")]
    [InlineData("bonus")]
    [InlineData("spellsFormula")]
    public void RequiredSchemaVersion_is_9_when_any_formula_field_reads_a_scale(string field)
    {
        var catalog = Fixtures.ChroniclerCatalog();
        var loremaster = Fixtures.MulticlassPack().Revisions.Single(r => r.Reference == Fixtures.Loremaster);
        Effect effect = field switch
        {
            "modifier" => new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "SCALE.ink" },
            "resource" => new ResourceEffect { Id = "r", ResourceId = "pool", Label = "Pool", Maximum = "SCALE.ink" },
            "recovery" => new RecoveryEffect { Id = "c", ResourceId = "pool", On = RestPeriod.LongRest, Amount = "SCALE.ink" },
            "cost" => new RollEffect { Id = "o", RollId = "o", Label = "O", Dice = "1d4", ResourceId = "pool", Cost = "SCALE.ink" },
            "bonus" => new RollEffect { Id = "b", RollId = "b", Label = "B", Dice = "1d4", Bonus = "SCALE.ink" }, // alone it would be v8
            _ => null!,
        };
        var revision = field == "spellsFormula"
            ? loremaster with
            {
                SchemaVersion = 9,
                Effects = [.. loremaster.Effects.Select(e => e is SpellcastingEffect s ? s with { SpellsTable = null, SpellsFormula = "SCALE.ink" } : e)],
            }
            : loremaster with { SchemaVersion = 9, Effects = [.. loremaster.Effects, effect] };

        Assert.Equal(9, ContentValidator.Validate(revision, catalog).RequiredSchemaVersion);
        Assert.Contains(ContentValidator.Validate(revision with { SchemaVersion = 8 }, catalog).Errors, e => e.Code == "validate.requires-v9");
    }

    [Fact]
    public void RequiredSchemaVersion_stays_below_9_for_a_class_that_uses_nothing_from_v9()
    {
        var loremaster = Fixtures.MulticlassPack().Revisions.Single(r => r.Reference == Fixtures.Loremaster);
        var bonus = loremaster with
        {
            SchemaVersion = 9,
            Effects = [.. loremaster.Effects, new RollEffect { Id = "b", RollId = "b", Label = "B", Dice = "1d4", Bonus = "PB" }],
        };
        Assert.Equal(7, ContentValidator.Validate(loremaster with { SchemaVersion = 9 }, Fixtures.ChroniclerCatalog()).RequiredSchemaVersion);
        Assert.Equal(8, ContentValidator.Validate(bonus, Fixtures.ChroniclerCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void A_v9_revision_is_refused_by_validation_and_calculation_that_support_only_v8()
    {
        // A build that supports v8 refuses v9 the way this build refuses v10: by version, before reading anything.
        var v10 = ChroniclerRevision() with { SchemaVersion = ContentRevision.CurrentSchemaVersion + 1 };
        Assert.Contains(ContentValidator.Validate(v10, Fixtures.ChroniclerCatalog()).Errors, e => e.Code == "validate.schema-unsupported");

        var catalog = new InMemoryContentCatalog([.. Fixtures.ChroniclerPack().Sources], [v10, .. Fixtures.ChroniclerPack().Revisions.Skip(1)]);
        var sheet = CharacterCalculator.Calculate(Chronicler(RulesFamilies.Srd521, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "content.schema-unsupported" && d.Content == Fixtures.Chronicler);
        Assert.Null(sheet.Scales);
    }

    [Fact]
    public void Validation_refuses_bad_scales_and_bad_caster_tables()
    {
        var catalog = Fixtures.ChroniclerCatalog();
        var chronicler = ChroniclerRevision();
        ContentRevision With(Func<Effect, Effect> change) => chronicler with { Effects = [.. chronicler.Effects.Select(change)] };
        IEnumerable<string> Errors(ContentRevision r) => ContentValidator.Validate(r, catalog).Errors.Select(e => e.Code);

        Assert.Contains("validate.scale-values", Errors(With(e => e is ScaleEffect s && s.ScaleId == "ink" ? s with { Values = [1, 2, 3] } : e)));
        Assert.Contains("validate.scale-values", Errors(With(e => e is ScaleEffect s && s.ScaleId == "ink" ? s with { Values = [.. s.Values.Take(19), 10_001] } : e)));
        Assert.Contains("validate.scale-incomplete", Errors(With(e => e is ScaleEffect s && s.ScaleId == "ink" ? s with { ScaleId = "Ink" } : e)));
        Assert.Contains("validate.scale-duplicate", Errors(With(e => e is ScaleEffect s && s.ScaleId == "lore" ? s with { ScaleId = "ink" } : e)));
        Assert.Contains("validate.scale-duplicate", Errors(With(e => e is ScaleEffect s && s.ScaleId == "lore" ? s with { ScaleId = "echo" } : e))); // its subclass's id

        var feature = Fixtures.ChroniclerPack().Revisions.Single(r => r.Reference == Fixtures.ChroniclerMarginalia);
        var misplaced = feature with { Effects = [.. feature.Effects, chronicler.Effects.OfType<ScaleEffect>().First() with { Id = "x" }] };
        Assert.Contains("validate.scale-kind", Errors(misplaced));

        SpellcastingEffect Table(IReadOnlyList<int> table) => chronicler.Effects.OfType<SpellcastingEffect>().Single() with { MulticlassCasterTable = table };
        Assert.Contains("validate.spellcasting-multiclass-table", Errors(With(e => e is SpellcastingEffect ? Table([0, 1]) : e)));
        Assert.Contains("validate.spellcasting-multiclass-table", Errors(With(e => e is SpellcastingEffect ? Table([.. Enumerable.Repeat(2, 20)]) : e))); // 2 at level 1
        Assert.Contains("validate.spellcasting-multiclass-table", Errors(With(e => e is SpellcastingEffect ? Table([0, 1, 1, 0, .. Enumerable.Repeat(1, 16)]) : e))); // decreases
        Assert.Contains("validate.spellcasting-multiclass-both", Errors(With(e => e is SpellcastingEffect s ? s with { MulticlassCaster = MulticlassCaster.Full } : e)));
        Assert.Contains("validate.spellcasting-multiclass-pact", Errors(With(e => e is SpellcastingEffect s ? s with { SlotKind = SpellSlotKind.PactMagic, Slots = [.. Enumerable.Repeat<IReadOnlyList<int>>([1], 20)] } : e)));
    }

    [Fact]
    public void A_formula_reading_a_scale_its_class_does_not_define_is_a_warning()
    {
        var chronicler = ChroniclerRevision();
        var typo = chronicler with
        {
            Effects = [.. chronicler.Effects.Select(e => e is ResourceEffect r ? r with { Maximum = "SCALE.inkk" } : e)],
        };
        var report = ContentValidator.Validate(typo, Fixtures.ChroniclerCatalog());
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Code == "validate.scale-unknown" && w.Message.Contains("SCALE.inkk", StringComparison.Ordinal));
    }

    [Fact]
    public void A_subclass_that_repeats_its_class_scale_id_loses_to_the_class_at_calculation()
    {
        var pack = Fixtures.ChroniclerPack();
        var echoes = pack.Revisions.Single(r => r.Reference == Fixtures.ArchiveOfEchoes);
        var clash = echoes with { Effects = [.. echoes.Effects.Select(e => e is ScaleEffect s ? s with { ScaleId = "ink", Values = [.. Enumerable.Repeat(99, 20)] } : e)] };
        var catalog = new InMemoryContentCatalog([.. pack.Sources, .. Fixtures.M1Pack().Sources], [.. pack.Revisions.Where(r => r.Reference != Fixtures.ArchiveOfEchoes), clash]);

        var sheet = CharacterCalculator.Calculate(Chronicler(RulesFamilies.Srd521, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "scale.duplicate" && d.Content == Fixtures.ArchiveOfEchoes);
        Assert.Equal(4, sheet.Resources!.Single(r => r.ResourceId == "ink").Maximum);
    }
}
