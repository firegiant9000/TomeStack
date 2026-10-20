using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M3 B2 (content schema v6, character schema v7): toggled (whileActive) modifiers, actions with a fixed or variable cost,
/// and a resource shared between features. Original fixtures: <c>fixture-pack-m3-effects.json</c>.
/// </summary>
public class ToggleAndCostTests
{
    private static Character Pinned(PlayState? play = null) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [Fixtures.RadiantStance, Fixtures.BorrowedSpark], Play = play ?? new PlayState() };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.AllCatalog());

    [Fact]
    public void A_toggled_modifier_applies_only_while_its_toggle_is_on_and_is_automatic_either_way()
    {
        var off = Sheet(Pinned());
        var on = Sheet(Pinned(new PlayState { Toggles = [new(Fixtures.RadiantStance.ContentId, "stance")] }));

        var acOff = off.Field(FieldIds.ArmorClass);
        var acOn = on.Field(FieldIds.ArmorClass);
        Assert.Equal(2, acOn.Value - acOff.Value);
        Assert.Equal((AutomationStatus.Automatic, AutomationStatus.Automatic), (acOff.Automation, acOn.Automation)); // the rules decide; nothing is left to the player
        Assert.Contains(acOn.Trace, t => t.Origin.EffectId == "stance-ac");
        Assert.Equal((false, true), (off.Toggles!.Single().On, on.Toggles!.Single().On));
        Assert.Equal("radiance", off.Toggles!.Single().ResourceId);
    }

    [Fact]
    public void Costs_are_evaluated_and_a_shared_resource_names_its_defining_content()
    {
        var sheet = Sheet(Pinned());
        var effects = sheet.Features!.SelectMany(f => f.Effects).Where(e => e.Type == "roll").ToDictionary(e => e.Id);

        Assert.Equal((1, false), (effects["flare"].Cost!.Value, effects["flare"].VariableCost));
        Assert.Equal((3, true), (effects["surge"].Cost!.Value, effects["surge"].VariableCost)); // PB 3 at level 5
        Assert.Equal((Fixtures.RadiantStance.ContentId, 1), (effects["spark"].ResourceContent!.Value, effects["spark"].Cost!.Value)); // default cost 1
        Assert.Single(sheet.Resources!, r => r.ResourceId == "radiance"); // one pool, however many features spend it
    }

    [Fact]
    public void A_long_rest_proposes_switching_active_toggles_off()
    {
        var character = Pinned(new PlayState { Toggles = [new(Fixtures.RadiantStance.ContentId, "stance")] });
        var plan = RestPlanner.LongRest(character, Sheet(character));

        var change = Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Toggle);
        Assert.Equal(("Radiant stance", 1, 0), (change.Label, change.From, change.To));
        Assert.Empty(RestPlanner.Apply(character.Play, plan, new HashSet<string>()).Toggles);
        Assert.Single(RestPlanner.Apply(character.Play, plan, new HashSet<string> { change.Id }).Toggles); // unticked: it stays on
    }

    [Fact]
    public void Validation_checks_toggles_and_costs_and_refuses_them_below_v6()
    {
        var catalog = Fixtures.AllCatalog();
        var stance = catalog.FindRevision(Fixtures.RadiantStance)!;
        var modifier = stance.Effects.OfType<ModifierEffect>().Single();

        Assert.True(ContentValidator.Validate(stance, catalog).CanPublish);
        Assert.True(ContentValidator.Validate(catalog.FindRevision(Fixtures.BorrowedSpark)!, catalog).CanPublish);
        var unknownToggle = stance with { RevisionId = Guid.NewGuid(), Effects = [.. stance.Effects.Select(e => e == modifier ? modifier with { Toggle = "nope" } : e)] };
        var wrongTiming = stance with { RevisionId = Guid.NewGuid(), Effects = [.. stance.Effects.Select(e => e == modifier ? modifier with { Timing = EffectTiming.Always } : e)] };
        Assert.Contains(ContentValidator.Validate(unknownToggle, catalog).Errors, e => e.Code == "validate.toggle-unknown");
        Assert.Contains(ContentValidator.Validate(wrongTiming, catalog).Errors, e => e.Code == "validate.toggle-timing");
        Assert.Contains(ContentValidator.Validate(stance with { RevisionId = Guid.NewGuid(), SchemaVersion = 5 }, catalog).Errors, e => e.Code == "validate.requires-v6");
    }

    [Fact]
    public void A_toggle_effect_in_a_revision_older_than_v6_stays_unknown_and_byte_for_byte()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack-m3-effects.json"));
        var stance = JsonSerializer.Deserialize<ContentPack>(json, RulesJson.Options)!.Revisions.Single(r => r.Reference == Fixtures.RadiantStance);
        Assert.IsType<ToggleEffect>(stance.Effects[2]);

        var v5 = JsonSerializer.Serialize(stance, RulesJson.Options).Replace("\"schemaVersion\": 6", "\"schemaVersion\": 5", StringComparison.Ordinal);
        var old = JsonSerializer.Deserialize<ContentRevision>(v5, RulesJson.Options)!;

        Assert.IsType<UnknownEffect>(old.Effects[2]);
        Assert.Equal(v5, JsonSerializer.Serialize(old, RulesJson.Options));
    }

    [Fact]
    public void A_whileActive_modifier_without_a_toggle_stays_assisted()
    {
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Untoggled Feat", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(Fixtures.EffectsPack().Sources[0].Id, new(3)), Status = RevisionStatus.Published,
            Effects = [new ModifierEffect { Id = "ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "1", Timing = EffectTiming.WhileActive }],
        };
        var pack = Fixtures.EffectsPack();
        var m1 = Fixtures.M1Pack();
        var catalog = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. m1.Sources, .. pack.Sources], [.. Fixtures.Pack().Revisions, .. m1.Revisions, feat]);

        var sheet = CharacterCalculator.Calculate(Fixtures.Load("srd521-ash-m1.json") with { Pins = [feat.Reference] }, catalog);

        Assert.Equal(AutomationStatus.Assisted, sheet.Field(FieldIds.ArmorClass).Automation);
    }

    // ---- Wave 3 item 20 (D38): Bloodied and auto toggles --------------------------------------------------------------

    internal static readonly Guid AutoContent = Guid.Parse("5fb10d00-0000-4000-8000-000000000001");

    /// <summary>An invented v10 feat with an auto toggle ("bloodied") and a +2 Armor Class modifier that applies while it is on.</summary>
    internal static ContentRevision AutoRevision(string family, int schema = 10, bool auto = true, IReadOnlyList<Effect>? more = null) => new()
    {
        ContentId = AutoContent, RevisionId = Guid.Parse("5fb10e00-0000-4000-8000-000000000001"), SchemaVersion = schema, Kind = ContentKind.Feat,
        Name = "Testy Grit", RulesFamilies = [family], Status = RevisionStatus.Published,
        Provenance = new(family == RulesFamilies.Srd51 ? Fixtures.Source2014 : Fixtures.Source2024, new(3)),
        Effects =
        [
            new ToggleEffect { Id = "grit-toggle", ToggleId = "grit", Label = "Testy grit", AutoWhen = auto ? ToggleAutoWhen.Bloodied : null },
            new ModifierEffect { Id = "grit-ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "2", Timing = EffectTiming.WhileActive, Toggle = "grit" },
            .. more ?? [],
        ],
    };

    internal static InMemoryContentCatalog CatalogWith(params ContentRevision[] extra)
    {
        var (m0, m1) = (Fixtures.Pack(), Fixtures.M1Pack());
        return new([.. m0.Sources, .. m1.Sources], [.. m0.Revisions, .. m1.Revisions, .. extra]);
    }

    /// <summary>The M1 hero of the family with the revision pinned, hit point maximum 21 by an override, and the given current / temporary hit points.</summary>
    internal static Character Hero(ContentRevision revision, int? current, int temporary = 0, int maximum = 21, PlayState? play = null) =>
        Fixtures.Load(revision.RulesFamilies[0] == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [revision.Reference],
            Overrides = [new FieldOverride(FieldIds.HitPoints, maximum, "Testy fixed maximum")],
            Play = (play ?? new PlayState()) with { CurrentHitPoints = current, TemporaryHitPoints = temporary },
        };

    [Theory]
    [InlineData(21, 21, false)]
    [InlineData(21, 11, false)] // odd maximum: half is 10.5, floored to 10
    [InlineData(21, 10, true)]
    [InlineData(21, 1, true)]
    [InlineData(21, 0, true)] // still bloodied at 0 (R168)
    [InlineData(20, 10, true)]
    [InlineData(20, 11, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    public void Bloodied_is_current_hit_points_at_or_below_half_the_maximum_rounded_down(int maximum, int current, bool expected)
    {
        var revision = AutoRevision(RulesFamilies.Srd521);
        var sheet = CharacterCalculator.Calculate(Hero(revision, current, maximum: maximum), CatalogWith(revision));

        Assert.Equal(maximum, sheet.HitPoints!.Maximum);
        Assert.Equal(expected, sheet.Bloodied == true);
    }

    [Fact]
    public void A_maximum_of_zero_is_never_bloodied_and_temporary_hit_points_do_not_count()
    {
        var revision = AutoRevision(RulesFamilies.Srd521);
        var catalog = CatalogWith(revision);

        Assert.NotEqual(true, CharacterCalculator.Calculate(Hero(revision, 0, maximum: 0), catalog).Bloodied);
        Assert.Equal(true, CharacterCalculator.Calculate(Hero(revision, 10, temporary: 15), catalog).Bloodied); // 10 + 15 temporary is above half; real hit points are not
        Assert.NotEqual(true, CharacterCalculator.Calculate(Hero(revision, 11, temporary: 5), catalog).Bloodied);
        Assert.NotEqual(true, CharacterCalculator.Calculate(Hero(revision, null), catalog).Bloodied); // undamaged: current is the maximum
    }

    [Fact]
    public void An_auto_toggle_is_on_while_bloodied_and_its_modifier_applies_exactly_then()
    {
        var revision = AutoRevision(RulesFamilies.Srd521);
        var catalog = CatalogWith(revision);
        var healthy = CharacterCalculator.Calculate(Hero(revision, 11), catalog);
        var bloodied = CharacterCalculator.Calculate(Hero(revision, 10), catalog);

        Assert.Equal(2, bloodied.Field(FieldIds.ArmorClass).Value - healthy.Field(FieldIds.ArmorClass).Value);
        Assert.Contains(bloodied.Field(FieldIds.ArmorClass).Trace, t => t.Origin.EffectId == "grit-ac");
        var (off, on) = (healthy.Toggles!.Single(), bloodied.Toggles!.Single());
        Assert.Equal((false, true, true, true), (off.On, on.On, off.Auto, on.Auto));
        Assert.Equal(AutomationStatus.Automatic, bloodied.Field(FieldIds.ArmorClass).Automation);
        Assert.Equal(21, bloodied.HitPoints!.Maximum);
    }

    [Fact]
    public void A_toggle_without_autoWhen_stays_manual_even_when_bloodied()
    {
        var manual = AutoRevision(RulesFamilies.Srd521, auto: false);
        var sheet = CharacterCalculator.Calculate(Hero(manual, 10), CatalogWith(manual));

        Assert.True(sheet.Bloodied); // the flag reports the hit points whatever the content says
        Assert.Equal((false, (bool?)null), (sheet.Toggles!.Single().On, sheet.Toggles!.Single().Auto));
    }

    [Fact]
    public void The_pre_toggle_maximum_decides_bloodied_and_an_auto_modifier_forced_through_never_feeds_back()
    {
        // A non-auto always-on modifier on the maximum counts (+10). An auto one is refused by validation; forced through here
        // to prove the calculator ignores it for the decision: bloodied comes from a first pass without auto toggles.
        var raise = new ModifierEffect { Id = "grit-max", Operation = ModifierOperation.Bonus, Target = FieldIds.HitPoints, Value = "10", Timing = EffectTiming.WhileActive, Toggle = "grit" };
        var steady = new ModifierEffect { Id = "steady-max", Operation = ModifierOperation.Bonus, Target = FieldIds.HitPoints, Value = "10" };
        var revision = AutoRevision(RulesFamilies.Srd521, more: [steady, raise]);
        var catalog = CatalogWith(revision);
        var steadyOnly = AutoRevision(RulesFamilies.Srd521, more: [steady]);
        var bare = AutoRevision(RulesFamilies.Srd521);
        var withoutSteady = CharacterCalculator.Calculate(Hero(bare, null) with { Overrides = [] }, CatalogWith(bare)).HitPoints!.Maximum;
        var natural = CharacterCalculator.Calculate(Hero(steadyOnly, null) with { Overrides = [] }, CatalogWith(steadyOnly)).HitPoints!.Maximum; // the pre-toggle maximum
        Assert.Equal(withoutSteady + 10, natural); // the non-auto modifier counts

        var half = natural / 2;
        var atHalf = CharacterCalculator.Calculate(Hero(revision, half) with { Overrides = [] }, catalog);
        Assert.True(atHalf.Bloodied);
        Assert.Equal((true, natural + 10), (atHalf.Toggles!.Single().On, atHalf.HitPoints!.Maximum)); // on, and the forced raise applies to the displayed maximum only

        // One above the pre-toggle half: with the raise included, (natural + 10) / 2 would be bloodied; it must not be.
        var above = CharacterCalculator.Calculate(Hero(revision, half + 1) with { Overrides = [] }, catalog);
        Assert.NotEqual(true, above.Bloodied);
        Assert.Equal((false, natural), (above.Toggles!.Single().On, above.HitPoints!.Maximum));
    }

    [Fact]
    public void A_stored_toggle_naming_an_auto_toggle_has_no_effect_in_either_direction()
    {
        var revision = AutoRevision(RulesFamilies.Srd521);
        var catalog = CatalogWith(revision);
        var stored = new PlayState { Toggles = [new(AutoContent, "grit")] };

        var healthy = CharacterCalculator.Calculate(Hero(revision, 11), catalog);
        var healthyStored = CharacterCalculator.Calculate(Hero(revision, 11, play: stored), catalog);
        Assert.False(healthyStored.Toggles!.Single().On);
        Assert.Equal(healthy.Field(FieldIds.ArmorClass).Value, healthyStored.Field(FieldIds.ArmorClass).Value);

        var bloodied = CharacterCalculator.Calculate(Hero(revision, 10), catalog);
        var bloodiedStored = CharacterCalculator.Calculate(Hero(revision, 10, play: stored), catalog);
        Assert.Equal(bloodied.Field(FieldIds.ArmorClass).Value, bloodiedStored.Field(FieldIds.ArmorClass).Value); // not counted twice
    }

    [Fact]
    public void In_a_v9_revision_autoWhen_is_extension_data_the_sheet_is_identical_and_the_toggle_is_manual()
    {
        // "Stored by an older build": the v10 revision's JSON re-read as v9, so autoWhen is carried as extension data.
        var json = JsonSerializer.Serialize(AutoRevision(RulesFamilies.Srd521), RulesJson.Options).Replace("\"schemaVersion\": 10", "\"schemaVersion\": 9", StringComparison.Ordinal);
        Assert.Contains("autoWhen", json, StringComparison.Ordinal);
        var v9 = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Options)!;
        Assert.Null(v9.Effects.OfType<ToggleEffect>().Single().AutoWhen); // not typed below v10
        var without = AutoRevision(RulesFamilies.Srd521, schema: 9, auto: false); // the same revision with no key at all

        string Run(ContentRevision revision, int current) =>
            JsonSerializer.Serialize(CharacterCalculator.Calculate(Hero(revision, current), CatalogWith(revision)), RulesJson.Options);

        foreach (var current in new[] { 21, 11, 10, 0 })
            Assert.Equal(Run(without, current), Run(v9, current));
        var sheet = CharacterCalculator.Calculate(Hero(v9, 10), CatalogWith(v9));
        Assert.Equal(CharacterCalculator.Calculate(Hero(without, 10), CatalogWith(without)).Field(FieldIds.ArmorClass).Value, sheet.Field(FieldIds.ArmorClass).Value); // no +2
        Assert.Equal((false, (bool?)null), (sheet.Toggles!.Single().On, sheet.Toggles!.Single().Auto));
        var switched = CharacterCalculator.Calculate(Hero(v9, 10, play: new PlayState { Toggles = [new(AutoContent, "grit")] }), CatalogWith(v9)); // a manual toggle, stored on
        Assert.Equal((true, sheet.Field(FieldIds.ArmorClass).Value + 2), (switched.Toggles!.Single().On, switched.Field(FieldIds.ArmorClass).Value));
    }

    [Fact]
    public void The_long_rest_proposal_leaves_an_auto_toggle_alone_and_still_proposes_a_manual_one()
    {
        var revision = AutoRevision(RulesFamilies.Srd521);
        var character = Hero(revision, 10, play: new PlayState { Toggles = [new(AutoContent, "grit")] });
        Assert.DoesNotContain(RestPlanner.LongRest(character, CharacterCalculator.Calculate(character, CatalogWith(revision))).Changes, c => c.Kind == RestChangeKind.Toggle);

        var manual = AutoRevision(RulesFamilies.Srd521, auto: false);
        var kept = Hero(manual, 10, play: new PlayState { Toggles = [new(AutoContent, "grit")] });
        Assert.Single(RestPlanner.LongRest(kept, CharacterCalculator.Calculate(kept, CatalogWith(manual))).Changes, c => c.Kind == RestChangeKind.Toggle);
    }

    [Fact]
    public void Validation_refuses_an_auto_toggle_that_spends_a_use_or_raises_hit_points_or_an_ability()
    {
        var resource = new ResourceEffect { Id = "uses", ResourceId = "uses", Label = "Testy uses", Maximum = "1" };
        var catalog = CatalogWith();
        var plain = AutoRevision(RulesFamilies.Srd521);
        Assert.True(ContentValidator.Validate(plain, catalog).CanPublish); // a plain Armor Class target is fine

        var spends = plain with { RevisionId = Guid.NewGuid(), Effects = [.. plain.Effects.Select(e => e is ToggleEffect t ? t with { ResourceId = "uses" } : e), resource] };
        Assert.Contains(ContentValidator.Validate(spends, catalog).Errors, e => e.Code == "validate.toggle-auto-resource");
        var manualSpends = spends with { Effects = [.. spends.Effects.Select(e => e is ToggleEffect t ? t with { AutoWhen = null } : e)] };
        Assert.DoesNotContain(ContentValidator.Validate(manualSpends, catalog).Errors, e => e.Code == "validate.toggle-auto-resource"); // a manual toggle may spend a use

        foreach (var target in new[] { FieldIds.HitPoints, FieldIds.Score(Ability.Con), FieldIds.Modifier(Ability.Con), FieldIds.Score(Ability.Str) })
        {
            var raises = AutoRevision(RulesFamilies.Srd521, more: [new ModifierEffect { Id = "grit-raise", Operation = ModifierOperation.Bonus, Target = target, Value = "2", Timing = EffectTiming.WhileActive, Toggle = "grit" }]);
            var result = ContentValidator.Validate(raises, catalog);
            Assert.Contains(result.Errors, e => e.Code == "validate.toggle-auto-target" && e.EffectId == "grit-raise");
            Assert.False(result.CanPublish);
        }
        // The same modifier on a manual toggle, and an always-on one, are not this rule's business.
        var manualRaise = AutoRevision(RulesFamilies.Srd521, auto: false, more: [new ModifierEffect { Id = "grit-raise", Operation = ModifierOperation.Bonus, Target = FieldIds.HitPoints, Value = "2", Timing = EffectTiming.WhileActive, Toggle = "grit" }]);
        Assert.DoesNotContain(ContentValidator.Validate(manualRaise, catalog).Errors, e => e.Code == "validate.toggle-auto-target");
    }
}
