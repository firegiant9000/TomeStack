using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M5 slice 7 (LIVING_SPECS D14): the four design hints, against the fixture full caster "Loremaster" as the bundled
/// baseline. Original content (prefix 5fd4). Hints are read-only opinions.
/// </summary>
public class DesignFeedbackTests
{
    private static readonly Guid Source = Guid.Parse("5fc05000-0000-4000-8000-000000000001");

    private static ContentRevision Class(params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("5fd4c000-0000-4000-8000-000000000001"),
        RevisionId = Guid.Parse("5fd4e000-0000-4000-8000-000000000001"),
        Kind = ContentKind.Class,
        Name = "Feedback Class",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Source),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static IReadOnlyList<ContentRevision> Baseline() => [Fixtures.MulticlassPack().Revisions.Single(r => r.Reference == Fixtures.Loremaster)];

    private static SpellcastingEffect Caster(IReadOnlyList<IReadOnlyList<int>> slots, MulticlassCaster? share = null) => new()
    {
        Id = "casting", Ability = Ability.Int, SpellList = "feedback", Slots = slots, MulticlassCaster = share,
    };

    private static IReadOnlyList<IReadOnlyList<int>> Rows(Func<int, IReadOnlyList<int>> row) => [.. Enumerable.Range(1, 20).Select(row)];

    [Fact]
    public void More_slots_than_any_full_caster_is_a_hint_at_the_first_level_under_each_family()
    {
        var loremaster = Baseline().Single().Effects.OfType<SpellcastingEffect>().Single();
        var generous = Class(Caster(Rows(l => [.. loremaster.Slots[l - 1].Select((n, i) => i == 0 ? n + 1 : n)]), MulticlassCaster.Full));

        var hints = DesignFeedback.Analyze(generous, Baseline()).Where(h => h.Code == "design.slots-above-full-caster").ToList();

        Assert.Equal([RulesFamilies.Srd51, RulesFamilies.Srd521], hints.Select(h => h.Family));
        Assert.All(hints, h => Assert.Equal((1, "casting"), (h.Level, h.EffectId)));
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(Caster(loremaster.Slots, MulticlassCaster.Full)), Baseline()), h => h.Code == "design.slots-above-full-caster");
    }

    [Fact]
    public void A_full_share_over_a_half_casters_table_is_a_hint()
    {
        // Slots from level 2 on, like a half caster, but it says every class level counts in multiclassing.
        var halfTable = Rows(l => l < 2 ? [] : [Math.Min(4, l)]);
        var hints = DesignFeedback.Analyze(Class(Caster(halfTable, MulticlassCaster.Full)), Baseline());

        var hint = Assert.Single(hints, h => h.Code == "design.multiclass-share-above-table" && h.Family == RulesFamilies.Srd51);
        Assert.Equal(1, hint.Level);
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(Caster(halfTable)), Baseline()), h => h.Code == "design.multiclass-share-above-table"); // never combined
    }

    [Fact]
    public void A_resource_is_a_hint_only_when_it_outgrows_every_SRD_pool_of_the_family()
    {
        // The baseline's fastest pool grows with the class level (+19 from level 1 to 20).
        var pool = Class(new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "CLASS_LEVEL" })
            with { ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Name = "Baseline Pool", Kind = ContentKind.Feature };
        var hints = DesignFeedback.Analyze(Class(
            new ResourceEffect { Id = "twice-level", ResourceId = "surge", Label = "Surge", Maximum = "2 * CLASS_LEVEL" },
            new ResourceEffect { Id = "per-level", ResourceId = "focus", Label = "Focus", Maximum = "CLASS_LEVEL" },
            new ResourceEffect { Id = "per-pb", ResourceId = "grit", Label = "Grit", Maximum = "PB" }), [pool]);

        var faster = hints.Where(h => h.Code == "design.resource-faster-than-srd").ToList();
        Assert.Equal(["twice-level", "twice-level"], faster.Select(h => h.EffectId)); // once per family
        Assert.All(faster, h => Assert.Contains("'Baseline Pool', grows by +19", h.Message, StringComparison.Ordinal));
        // Growing faster than the proficiency bonus alone is no longer a hint, and with no SRD pool there is nothing to compare.
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(new ResourceEffect { Id = "per-level", ResourceId = "focus", Label = "Focus", Maximum = "CLASS_LEVEL" }), [pool]), h => h.Code == "design.resource-faster-than-srd");
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(new ResourceEffect { Id = "x", ResourceId = "x", Label = "X", Maximum = "9 * CLASS_LEVEL" }), []), h => h.Code == "design.resource-faster-than-srd");
    }

    [Fact]
    public void The_SRD_pool_baseline_is_per_family_and_counts_only_pools_that_calculate()
    {
        ContentRevision Pool(string name, string maximum, string family, AutomationStatus automation = AutomationStatus.Automatic) =>
            Class(new ResourceEffect { Id = "pool", ResourceId = "pool", Label = name, Maximum = maximum, Automation = automation })
                with { ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Name = name, Kind = ContentKind.Feature, RulesFamilies = [family] };
        var baseline = new[]
        {
            Pool("Slow 2014 Pool", "CLASS_LEVEL", RulesFamilies.Srd51), // +19
            Pool("Fast 2024 Pool", "5 * CLASS_LEVEL", RulesFamilies.Srd521), // +95
            Pool("Reference 2014 Pool", "50 * CLASS_LEVEL", RulesFamilies.Srd51, AutomationStatus.Reference), // never calculated
            Pool("Broken 2014 Pool", "SCALE.missing * 100", RulesFamilies.Srd51), // does not evaluate here
        };
        var twice = Class(new ResourceEffect { Id = "twice", ResourceId = "twice", Label = "Twice", Maximum = "2 * CLASS_LEVEL" });

        var hint = Assert.Single(DesignFeedback.Analyze(twice, baseline), h => h.Code == "design.resource-faster-than-srd");
        Assert.Equal(RulesFamilies.Srd51, hint.Family); // faster than every 2014 pool, not than the 2024 one
        Assert.Contains("'Slow 2014 Pool', grows by +19", hint.Message, StringComparison.Ordinal);
    }

    private static Effect[] GrantsAt(IEnumerable<int> levels)
    {
        var feature = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        return [.. levels.Select(l => new GrantEffect { Id = $"at-{l}", Grant = GrantKind.Content, Content = feature, Level = l })];
    }

    [Fact]
    public void A_level_with_no_feature_is_a_hint_only_where_every_bundled_class_gains_something()
    {
        var everyLevel = Class(GrantsAt(Enumerable.Range(1, 20))) with { ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Name = "Baseline Busy" };
        var gapAtTwo = Class(GrantsAt(Enumerable.Range(1, 20).Where(l => l != 2)));

        var hint = Assert.Single(DesignFeedback.Analyze(gapAtTwo, [everyLevel]), h => h.Code == "design.level-without-feature" && h.Family == RulesFamilies.Srd51);
        Assert.StartsWith("Class level(s) 2 give", hint.Message, StringComparison.Ordinal);

        // A bundled class that also gains nothing at level 2 makes level 2 ordinary: no hint.
        var quietAtTwo = everyLevel with { ContentId = Guid.NewGuid(), Name = "Baseline Quiet", Effects = GrantsAt(Enumerable.Range(1, 20).Where(l => l != 2)) };
        Assert.DoesNotContain(DesignFeedback.Analyze(gapAtTwo, [everyLevel, quietAtTwo]), h => h.Code == "design.level-without-feature");
        Assert.DoesNotContain(DesignFeedback.Analyze(gapAtTwo, []), h => h.Code == "design.level-without-feature"); // nothing to compare with
    }

    [Fact]
    public void Half_and_third_shares_follow_each_familys_rounding_and_a_v9_table_counts_exactly()
    {
        // A 2014-style half caster: no slots at level 1. Rounded up (2024 rules), level 1 already counts one caster level.
        // Its own table is exactly the Multiclass Spellcaster row at half its level rounded down, so only the rounding differs.
        var multiclass = RulesFamilies.Get(RulesFamilies.Srd51).MulticlassSpellSlots;
        var halfTable = Rows(l => l < 2 ? [] : multiclass[(l / 2) - 1]);
        var half = DesignFeedback.Analyze(Class(Caster(halfTable, MulticlassCaster.Half)), Baseline()).Where(h => h.Code == "design.multiclass-share-above-table").ToList();
        Assert.Equal([(RulesFamilies.Srd521, (int?)1)], half.Select(h => (h.Family!, h.Level)));

        // A third caster without slots until level 7 still counts a caster level from level 3.
        var third = DesignFeedback.Analyze(Class(Caster(Rows(l => l < 7 ? [] : [2]), MulticlassCaster.Third)), Baseline());
        Assert.All(third.Where(h => h.Code == "design.multiclass-share-above-table"), h => Assert.Equal(3, h.Level));
        Assert.Equal(2, third.Count(h => h.Code == "design.multiclass-share-above-table"));

        // A v9 table that counts every level, over a thin slot table.
        var tabled = new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "feedback", Slots = Rows(l => l < 3 ? [] : [2]), MulticlassCasterTable = [.. Enumerable.Range(1, 20)] };
        Assert.Contains(DesignFeedback.Analyze(Class(tabled), Baseline()), h => h.Code == "design.multiclass-share-above-table" && h.Level == 1);
    }

    [Fact]
    public void Only_the_spellcasting_the_calculator_uses_gets_slot_hints_and_malformed_tables_none()
    {
        var generous = Rows(_ => [9]);
        // Reference only: the calculator never grants these slots.
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(Caster(generous, MulticlassCaster.Full) with { Automation = AutomationStatus.Reference }), Baseline()), h => h.Code.StartsWith("design.slots", StringComparison.Ordinal));
        // The first calculated caster is Pact Magic, so a second, ordinary one is ignored (as the calculator ignores it).
        var pact = Caster(Rows(_ => [0, 2]), null) with { Id = "pact", SlotKind = SpellSlotKind.PactMagic };
        Assert.DoesNotContain(DesignFeedback.Analyze(Class(pact, Caster(generous, MulticlassCaster.Full)), Baseline()), h => h.Code.StartsWith("design.slots", StringComparison.Ordinal));
        // Malformed tables (a missing row, a row past spell level 9) get no hint and no exception.
        IReadOnlyList<IReadOnlyList<int>> withNull = [.. Rows(_ => [9]).Select((r, i) => i == 4 ? null! : r)];
        IReadOnlyList<IReadOnlyList<int>> tooLong = Rows(_ => [2, 0, 0, 0, 0, 0, 0, 0, 0, 1]);
        foreach (var slots in new[] { withNull, tooLong })
            Assert.DoesNotContain(DesignFeedback.Analyze(Class(Caster(slots, MulticlassCaster.Full)), Baseline()), h => h.Code.StartsWith("design.", StringComparison.Ordinal) && h.EffectId == "casting");
    }

    [Fact]
    public void The_analyzer_changes_nothing_it_reads()
    {
        var revision = Class(Caster(Rows(l => [l]), MulticlassCaster.Full), new ResourceEffect { Id = "r", ResourceId = "r", Label = "R", Maximum = "LEVEL" });
        var before = System.Text.Json.JsonSerializer.Serialize(revision, RulesJson.Options);

        Assert.NotEmpty(DesignFeedback.Analyze(revision, Baseline()));

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(revision, RulesJson.Options));
    }
}
