using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 8, Task 8.1 (D43, content schema v11): the roll's <c>alsoChange</c> (a use that also spends or regains
/// other pools) and <c>drawsDown</c> (a cost that uses a counting-up pool's built-up value instead of adding to it): the
/// version gate that keeps older revisions byte for byte, validation, the fixture pack, and the calculated sheet.
/// </summary>
public class ChargingContentTests
{
    private static ValidationReport Validate(ContentRevision revision, IEnumerable<ContentRevision>? batch = null) =>
        ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog(), batch);

    private static readonly Guid AbsentContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000aa"); // installed nowhere

    private static ContentRevision Beam() => Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.GatheringBeam)!;

    private static ContentRevision Change(string rollId, Func<RollEffect, RollEffect> change)
    {
        var revision = Beam();
        return revision with { Effects = [.. revision.Effects.Select(e => e is RollEffect roll && roll.Id == rollId ? change(roll) : e)] };
    }

    private static AlsoChange Charge(string resourceId = "charge", Guid? content = null, UpkeepChange change = UpkeepChange.Spend, string amount = "1") =>
        new(resourceId, content ?? Fixtures.ChargeAdept.ContentId, change, amount);

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_roll_carrying_alsoChange_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","automation":"automatic","timing":"always","alsoChange":[{"resourceId":"charge","change":"spend","amount":"1"}],"drawsDown":true}""";
        var revision = Fixtures.RevisionWithEffect(json, version);
        var roll = Assert.IsType<RollEffect>(revision.Effects.Single());

        Assert.Null(roll.AlsoChange);
        Assert.Null(roll.DrawsDown);
        Assert.Equal(json, Fixtures.EffectJson(roll));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(RevisionHashProperties.Hash(stored), RevisionHashProperties.Hash(RevisionHashProperties.Write(RevisionHashProperties.Read(stored))));
        Assert.Contains(json, stored);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(6)]
    public void A_v10_roll_carrying_only_drawsDown_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","automation":"automatic","timing":"always","drawsDown":false}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(roll.DrawsDown);
        Assert.Equal(json, Fixtures.EffectJson(roll));
    }

    [Theory]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"charge","change":"Spend ","amount":"1"}]}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"charge","change":"spend,regain","amount":"1"}]}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"charge","change":"gain","amount":"1"}]}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"charge","amount":"1"}]}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":[{"resourceId":"charge","change":"spend","amount":"1"},{"resourceId":"focus","change":" regain","amount":"1"}]}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","alsoChange":"charge"}""")]
    public void A_bad_alsoChange_change_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_v11_alsoChange_is_typed_and_round_trips()
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","automation":"automatic","timing":"always","alsoChange":[{"resourceId":"charge","resourceContent":"6f0dc000-0000-4000-8000-000000000080","change":"regain","amount":"PB"}],"drawsDown":true}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal([new AlsoChange("charge", Fixtures.ChargeAdept.ContentId, UpkeepChange.Regain, "PB")], roll.AlsoChange);
        Assert.True(roll.DrawsDown);
        var written = Fixtures.EffectJson(roll);
        Assert.Contains("\"alsoChange\":[{\"resourceId\":\"charge\",\"resourceContent\":\"6f0dc000-0000-4000-8000-000000000080\",\"change\":\"regain\",\"amount\":\"PB\"}]", written);
        Assert.Contains("\"drawsDown\":true", written);
        Assert.DoesNotContain("null", written);
        Assert.Equal(written, Fixtures.EffectJson(Fixtures.RevisionWithEffect(written, 11).Effects.Single()));
    }

    [Fact]
    public void The_roll_v11_key_list_has_the_charging_keys()
    {
        var keys = (IEnumerable<string>)typeof(RollEffect).GetField("V11Keys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

        Assert.Contains("alsoChange", keys);
        Assert.Contains("drawsDown", keys);
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var stance = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.RadiantStance)!;

        Assert.Equal(6, ContentValidator.Validate(stance with { SchemaVersion = 11 }, Fixtures.PlayMechanicsCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void Either_charging_key_requires_v11_in_a_revision_declaring_less()
    {
        var alsoChange = Change("gather", r => r) with { SchemaVersion = 10 };
        var drawsDown = Change("release", r => r with { AlsoChange = null }) with { SchemaVersion = 10 };

        Assert.Contains(Validate(alsoChange).Errors, e => e.Code == "validate.requires-v11");
        Assert.Contains(Validate(drawsDown).Errors, e => e.Code == "validate.requires-v11");
    }

    // ---- validation ----------------------------------------------------------------------------------------------------

    public static TheoryData<string> MistakeVariants =>
    [
        "also-blank-id", "also-unknown-pool", "also-unknown-pool-own-content", "also-content-not-installed",
        "also-amount-blank", "also-amount-null", "also-amount-invalid", "also-too-many", "also-names-own-pool",
        "draws-down-normal-pool", "draws-down-no-resource", "draws-down-unknown-pool",
    ];

    private static (ContentRevision Revision, string Code, string EffectId) Mistake(string variant) => variant switch
    {
        "also-blank-id" => (Change("gather", r => r with { AlsoChange = [Charge(" ")] }), "validate.also-change", "gather"),
        "also-unknown-pool" => (Change("gather", r => r with { AlsoChange = [Charge("nope")] }), "validate.also-change", "gather"),
        // No resourceContent: the pool is looked for in the beam's own content, which defines none.
        "also-unknown-pool-own-content" => (Change("gather", r => r with { AlsoChange = [new AlsoChange("charge", null, UpkeepChange.Spend, "1")] }), "validate.also-change", "gather"),
        "also-content-not-installed" => (Change("gather", r => r with { AlsoChange = [Charge(content: AbsentContent)] }), "validate.also-change", "gather"),
        "also-amount-blank" => (Change("gather", r => r with { AlsoChange = [Charge(amount: "  ")] }), "validate.also-change", "gather"),
        "also-amount-null" => (Change("gather", r => r with { AlsoChange = [new AlsoChange("charge", Fixtures.ChargeAdept.ContentId, UpkeepChange.Spend, null!)] }), "validate.also-change", "gather"),
        "also-amount-invalid" => (Change("gather", r => r with { AlsoChange = [Charge(amount: "1 +")] }), "validate.also-change", "gather"),
        "also-too-many" => (Change("gather", r => r with { AlsoChange = [Charge(), Charge(), Charge(), Charge(), Charge()] }), "validate.also-change", "gather"),
        "also-names-own-pool" => (Change("gather", r => r with { AlsoChange = [Charge("focus")] }), "validate.also-change", "gather"),
        "draws-down-normal-pool" => (Change("release", r => r with { ResourceId = "focus" }), "validate.draws-down", "release"),
        "draws-down-no-resource" => (Change("release", r => r with { ResourceId = null, ResourceContent = null, Cost = null, PerUnit = null }), "validate.draws-down", "release"),
        "draws-down-unknown-pool" => (Change("release", r => r with { ResourceId = "nope" }), "validate.draws-down", "release"),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    [Theory]
    [MemberData(nameof(MistakeVariants))]
    public void Charging_mistakes_are_errors(string variant)
    {
        var (revision, code, effectId) = Mistake(variant);

        var errors = Validate(revision).Errors.Where(e => e.Code == code).ToList();

        Assert.Contains(errors, e => e.EffectId == effectId);
    }

    [Fact]
    public void A_charging_message_names_ids_and_counts_never_user_text()
    {
        var errors = Validate(Mistake("also-too-many").Revision).Errors.Where(e => e.Code == "validate.also-change").ToList();

        Assert.Contains(errors, e => e.Message.Contains('5', StringComparison.Ordinal) && e.Message.Contains('4', StringComparison.Ordinal));
    }

    [Fact]
    public void Four_alsoChange_entries_and_a_false_drawsDown_are_accepted()
    {
        var four = Change("gather", r => r with { AlsoChange = [Charge(), Charge(change: UpkeepChange.Regain), Charge(), Charge()] });
        var no = Change("release", r => r with { DrawsDown = false, ResourceId = "focus" });

        Assert.DoesNotContain(Validate(four).Errors, e => e.Code == "validate.also-change");
        Assert.DoesNotContain(Validate(no).Errors, e => e.Code == "validate.draws-down");
    }

    [Fact]
    public void An_entry_naming_the_rolls_own_pool_is_refused_but_the_same_id_in_another_content_is_not()
    {
        // Own pool: the roll spends focus of the adept, and the entry names focus of the adept.
        Assert.Contains(Validate(Mistake("also-names-own-pool").Revision).Errors, e => e.Code == "validate.also-change" && e.Message.Contains("focus", StringComparison.Ordinal));

        // The same id in this content (the beam defines a pool `focus` of its own here) is a different pool from the adept's.
        var beam = Beam();
        var other = beam with
        {
            Effects = [.. beam.Effects.Select(e => e is RollEffect { Id: "gather" } r ? r with { AlsoChange = [new AlsoChange("focus", null, UpkeepChange.Spend, "1")] } : e),
                new ResourceEffect { Id = "own-focus", ResourceId = "focus", Label = "Own focus", Maximum = "2" }],
        };

        Assert.DoesNotContain(Validate(other).Errors, e => e.Code == "validate.also-change");
    }

    [Fact]
    public void A_null_alsoChange_item_is_an_empty_entry()
    {
        var revision = Change("gather", r => r with { AlsoChange = [null!] });

        Assert.Contains(ContentValidator.EmptyEntries(revision), d => d.Code == "validate.empty-entry" && d.Message.Contains("alsoChange", StringComparison.Ordinal));
        Assert.Empty(ContentValidator.EmptyEntries(Beam()));
    }

    [Fact]
    public void DrawsDown_suppresses_the_counting_up_cost_warning()
    {
        var report = Validate(Beam());

        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.cost-counts-up");
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.draws-down");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void A_cost_on_a_counting_up_pool_without_drawsDown_still_warns(bool? drawsDown)
    {
        var report = Validate(Change("release", r => r with { DrawsDown = drawsDown }));

        Assert.Contains(report.Warnings, w => w.Code == "validate.cost-counts-up" && w.EffectId == "release");
    }

    [Fact]
    public void DrawsDown_only_suppresses_the_warning_for_its_own_roll()
    {
        var beam = Beam();
        var second = beam.Effects.OfType<RollEffect>().Single(r => r.Id == "release") with { Id = "release-again", RollId = "release-again", DrawsDown = null };
        var both = beam with { Effects = [.. beam.Effects, second] };

        var warnings = Validate(both).Warnings.Where(w => w.Code == "validate.cost-counts-up").ToList();

        Assert.Equal(["release-again"], warnings.Select(w => w.EffectId));
    }

    // ---- the fixture pack ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_charging_pack_validates_clean_and_types_both_rolls()
    {
        var batch = Fixtures.ChargingPack().Revisions.ToList();
        foreach (var reference in new[] { Fixtures.ChargeAdept, Fixtures.GatheringBeam })
        {
            var revision = Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;
            Assert.DoesNotContain(revision.Effects, e => e is UnknownEffect);
            var report = Validate(revision, batch);
            Assert.True(report.Errors.Count == 0, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.True(report.Warnings.Count == 0, $"{revision.Name}: {string.Join("; ", report.Warnings.Select(e => $"{e.Code} {e.Message}"))}");
        }
        Assert.Equal(11, Validate(Beam(), batch).RequiredSchemaVersion);

        var rolls = Beam().Effects.OfType<RollEffect>().ToDictionary(r => r.Id);
        Assert.Null(rolls["gather"].Dice);
        Assert.Equal(Activation.BonusAction, rolls["gather"].Activation);
        Assert.Equal([new AlsoChange("charge", Fixtures.ChargeAdept.ContentId, UpkeepChange.Spend, "1")], rolls["gather"].AlsoChange);
        Assert.Null(rolls["gather"].DrawsDown);
        Assert.Equal("2d8", rolls["release"].Dice);
        Assert.Equal(new RollPerUnit("1d8", 4), rolls["release"].PerUnit);
        Assert.True(rolls["release"].DrawsDown);
        Assert.Null(rolls["release"].AlsoChange);

        var written = Fixtures.EffectJson(rolls["gather"]);
        Assert.Equal(written, Fixtures.EffectJson(Fixtures.RevisionWithEffect(written, 11).Effects.Single()));
        var writtenRelease = Fixtures.EffectJson(rolls["release"]);
        Assert.Contains("\"drawsDown\":true", writtenRelease);
        Assert.Equal(writtenRelease, Fixtures.EffectJson(Fixtures.RevisionWithEffect(writtenRelease, 11).Effects.Single()));
    }

    // ---- carried from the 8.1 review -----------------------------------------------------------------------------------

    [Fact]
    public void An_alsoChange_amount_counts_as_a_formula_read()
    {
        var revision = Change("gather", r => r with { AlsoChange = [Charge(amount: "SCALE.x + VALUE.y")] });

        var read = ContentGraph.Identifiers(revision).Where(i => i.EffectId == "gather").Select(i => i.Identifier).ToList();

        Assert.Contains("SCALE.x", read);
        Assert.Contains("VALUE.y", read);
        Assert.Contains(ContentGraph.Formulas(revision), f => f.EffectId == "gather" && f.Source == "SCALE.x + VALUE.y");
    }

    [Fact]
    public void A_pool_changed_only_by_another_contents_alsoChange_is_not_dead()
    {
        var source = Fixtures.ChargingPack().Sources[0].Id;
        ContentRevision Draft(int n, params Effect[] effects) => new()
        {
            ContentId = Guid.Parse($"6f0dc000-0000-4000-8000-{n:X12}"),
            RevisionId = Guid.Parse($"6f0de000-0000-4000-8000-{n:X12}"),
            SchemaVersion = 11,
            Kind = ContentKind.Feature,
            Name = $"Debug {n:X}",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source),
            Status = RevisionStatus.Draft,
            Effects = effects,
        };
        var pool = Draft(0x90, new ResourceEffect { Id = "store", ResourceId = "store", Label = "Store", Maximum = "3" });
        var changer = Draft(0x91, new RollEffect
        {
            Id = "fill", RollId = "fill", Label = "Fill", Dice = "1d4",
            AlsoChange = [new AlsoChange("store", pool.ContentId, UpkeepChange.Spend, "1")],
        });
        var stored = Fixtures.ChargingPack().Revisions;
        var catalog = new InMemoryContentCatalog(Fixtures.ChargingPack().Sources, [.. stored, pool, changer]);

        var alone = ContentDebugger.Diagnose([pool], stored, new InMemoryContentCatalog(Fixtures.ChargingPack().Sources, [.. stored, pool]));
        var together = ContentDebugger.Diagnose([pool, changer], stored, catalog);

        Assert.Contains(alone.Findings, f => f.Code == "debug.resource-dead" && f.EffectId == "store");
        Assert.DoesNotContain(together.Findings, f => f.Code == "debug.resource-dead");
    }

    // ---- the calculated sheet ------------------------------------------------------------------------------------------

    private static CharacterSheet Sheet(InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(Fixtures.Load("pm-charger-l5.json"), catalog ?? Fixtures.PlayMechanicsCatalog());

    private static FeatureEffect Effect(CharacterSheet sheet, string id) =>
        sheet.Features!.Single(f => f.Content == Fixtures.GatheringBeam).Effects.Single(e => e.Id == id);

    private static InMemoryContentCatalog WithGather(Func<RollEffect, RollEffect> change)
    {
        var pack = Fixtures.ChargingPack();
        var revisions = pack.Revisions
            .Select(r => r.Reference == Fixtures.GatheringBeam
                ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? change(roll) : e)] }
                : r)
            .ToList();
        return new([.. Fixtures.Pack().Sources, .. pack.Sources], [.. Fixtures.Pack().Revisions, .. revisions]);
    }

    [Fact]
    public void The_charger_has_both_pools_with_nothing_spent()
    {
        var sheet = Sheet();

        var focus = sheet.Resources!.Single(r => r.ResourceId == "focus");
        var charge = sheet.Resources!.Single(r => r.ResourceId == "charge");
        Assert.Equal(9, focus.Maximum);
        Assert.Equal(0, focus.Spent);
        Assert.False(focus.CountsUp);
        Assert.Equal(4, charge.Maximum);
        Assert.Equal(0, charge.Spent);
        Assert.True(charge.CountsUp);
        Assert.Equal(8, Fixtures.Load("pm-charger-l5.json").SchemaVersion);
    }

    [Fact]
    public void The_sheet_evaluates_alsoChange_amounts()
    {
        var sheet = Sheet();

        var gather = Effect(sheet, "gather");
        var change = Assert.Single(gather.AlsoChange!);
        Assert.Equal(new AlsoChangeValue(Fixtures.ChargeAdept.ContentId, "charge", UpkeepChange.Spend, 1), change);
        Assert.False(gather.DrawsDown);
        Assert.Equal(1, gather.Cost);
        Assert.Equal(AutomationStatus.Automatic, gather.Automation);

        var release = Effect(sheet, "release");
        Assert.True(release.DrawsDown);
        Assert.Null(release.AlsoChange);
        Assert.Equal(0, release.Cost);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "effect.invalid-formula");
    }

    [Fact]
    public void An_alsoChange_amount_reads_the_character_and_never_goes_below_zero()
    {
        var sheet = Sheet(WithGather(r => r with { AlsoChange = [Charge(amount: "PB - 3"), Charge(amount: "PB"), Charge(amount: "0 - 2")] }));

        Assert.Equal([0, 3, 0], Effect(sheet, "gather").AlsoChange!.Select(c => c.Amount));
    }

    [Fact]
    public void An_alsoChange_content_defaults_to_the_rolls_own_content()
    {
        var sheet = Sheet(WithGather(r => r with { AlsoChange = [new AlsoChange("charge", null, UpkeepChange.Regain, "1")] }));

        var change = Assert.Single(Effect(sheet, "gather").AlsoChange!);
        Assert.Equal(Fixtures.GatheringBeam.ContentId, change.ResourceContent);
        Assert.Equal(UpkeepChange.Regain, change.Change);
    }

    [Fact]
    public void A_failing_alsoChange_amount_is_null_with_a_warning_and_the_row_is_assisted()
    {
        var sheet = Sheet(WithGather(r => r with { AlsoChange = [Charge(amount: "1 + SCALE.nonesuch"), Charge(amount: "2")] }));

        var gather = Effect(sheet, "gather");
        Assert.Equal([null, 2], gather.AlsoChange!.Select(c => c.Amount));
        Assert.Equal(AutomationStatus.Assisted, gather.Automation);
        Assert.True(gather.Available);
        Assert.Single(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.GatheringBeam && d.EffectId == "gather");
    }
}
