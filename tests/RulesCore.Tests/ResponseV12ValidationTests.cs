namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G6): validation of a response's trigger, share, dice, automatic, unlessCondition and supersedes, and the
/// validate.requires-v12 rung each one registers.
/// </summary>
public class ResponseV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e0200-0000-4000-8000-000000000001");

    private static ResponseEffect Reduction(Func<ResponseEffect, ResponseEffect>? shape = null, string? amount = "3")
    {
        var response = new ResponseEffect { Id = "r", ResponseId = "ward", Label = "Ward", Phase = ResponsePhase.DamageReduction, Amount = amount };
        return shape is null ? response : shape(response);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e0200-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Ward", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(ResponseEffect response, int version = 12) => ContentValidator.Validate(Built(version, response), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    private static ResponseTrigger SaveTrigger(string? ability = "dex", SaveResult? result = SaveResult.Success, IReadOnlyList<string>? types = null) =>
        new(ResponseSource.Save, ability, result, types);

    [Fact]
    public void V11_response_without_v12_members_still_requires_11()
    {
        var report = Validate(Reduction(), 11);
        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Draft_with_a_trigger_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var response = Reduction(r => r with { Trigger = new(ResponseSource.Attack) });
        var below = Validate(response, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a response trigger", StringComparison.Ordinal));
        AssertClean(Validate(response));
    }

    [Fact]
    public void Trigger_save_with_ability_and_result_is_valid()
    {
        AssertClean(Validate(Reduction(r => r with { Trigger = SaveTrigger(types: ["fire", "cold"]) })));
    }

    [Fact]
    public void Trigger_save_without_a_result_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = SaveTrigger(result: null) })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_save_without_an_ability_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = SaveTrigger(ability: null) })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_attack_with_an_ability_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = new(ResponseSource.Attack, "dex") })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_fall_with_a_result_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = new(ResponseSource.Fall, Result: SaveResult.Failure) })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_unknown_ability_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = SaveTrigger(ability: "luck") })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_empty_damageTypes_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = new(ResponseSource.Attack, DamageTypes: []) })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_duplicate_damageTypes_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = new(ResponseSource.Attack, DamageTypes: ["fire", "fire"]) })), "validate.response-trigger");
    }

    [Fact]
    public void Trigger_unknown_damage_type_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Trigger = new(ResponseSource.Attack, DamageTypes: ["sound"]) })), "validate.response-trigger");
    }

    [Fact]
    public void Share_on_a_damage_reduction_without_an_amount_is_valid()
    {
        var report = Validate(Reduction(r => r with { Share = ResponseShare.Half }, amount: null));
        AssertClean(report);
        Assert.Equal(12, Validate(Reduction(r => r with { Share = ResponseShare.All }, amount: null)).RequiredSchemaVersion);
    }

    [Fact]
    public void Share_below_v12_requires_v12()
    {
        var report = Validate(Reduction(r => r with { Share = ResponseShare.Half }, amount: null), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a response share", StringComparison.Ordinal));
    }

    [Fact]
    public void Share_with_an_amount_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Share = ResponseShare.Half })), "validate.response-amount");
    }

    [Fact]
    public void Share_on_a_hit_point_outcome_is_refused()
    {
        var response = new ResponseEffect { Id = "r", ResponseId = "ward", Label = "Ward", Phase = ResponsePhase.HitPointOutcome, Amount = "1", Share = ResponseShare.All };
        AssertError(Validate(response), "validate.response-share");
    }

    [Fact]
    public void Share_on_a_threshold_is_refused()
    {
        var response = new ResponseEffect { Id = "r", ResponseId = "ward", Label = "Ward", Phase = ResponsePhase.Threshold, When = new ResponseWhen(null, true), Share = ResponseShare.All };
        AssertError(Validate(response), "validate.response-share");
    }

    [Fact]
    public void Damage_reduction_with_neither_amount_nor_share_is_refused()
    {
        AssertError(Validate(Reduction(amount: null)), "validate.response-amount");
    }

    [Fact]
    public void Dice_on_a_damage_reduction_with_an_amount_is_valid()
    {
        AssertClean(Validate(Reduction(r => r with { Dice = "1d10" })));
    }

    [Fact]
    public void Dice_below_v12_requires_v12()
    {
        var report = Validate(Reduction(r => r with { Dice = "1d10" }), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("response dice", StringComparison.Ordinal));
    }

    [Fact]
    public void Dice_that_do_not_parse_are_refused()
    {
        AssertError(Validate(Reduction(r => r with { Dice = "lots" })), "validate.response-dice");
    }

    [Fact]
    public void Dice_with_a_share_are_refused()
    {
        AssertError(Validate(Reduction(r => r with { Dice = "1d4", Share = ResponseShare.Half }, amount: null)), "validate.response-dice");
    }

    [Fact]
    public void Dice_on_a_hit_point_outcome_are_refused()
    {
        var response = new ResponseEffect { Id = "r", ResponseId = "ward", Label = "Ward", Phase = ResponsePhase.HitPointOutcome, Amount = "1", Dice = "1d4" };
        AssertError(Validate(response), "validate.response-dice");
    }

    [Fact]
    public void Automatic_true_on_a_damage_reduction_is_valid()
    {
        AssertClean(Validate(Reduction(r => r with { Automatic = true })));
    }

    [Fact]
    public void Automatic_false_is_allowed_and_still_needs_v12()
    {
        var response = Reduction(r => r with { Automatic = false });
        AssertClean(Validate(response));
        var below = Validate(response, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("an automatic response", StringComparison.Ordinal));
    }

    [Fact]
    public void Automatic_on_a_hit_point_outcome_is_refused()
    {
        var response = new ResponseEffect { Id = "r", ResponseId = "ward", Label = "Ward", Phase = ResponsePhase.HitPointOutcome, Amount = "1", Automatic = true };
        AssertError(Validate(response), "validate.response-automatic");
    }

    [Fact]
    public void Automatic_with_a_cost_is_refused()
    {
        var response = Reduction(r => r with { Automatic = true, ResourceId = "pool", Cost = "1" });
        AssertError(ContentValidator.Validate(Built(12, response, new ResourceEffect { Id = "p", ResourceId = "pool", Label = "Pool", Maximum = "2" }), Catalog), "validate.response-automatic");
    }

    [Fact]
    public void UnlessCondition_a_condition_key_is_valid()
    {
        var response = Reduction(r => r with { UnlessCondition = "incapacitated" });
        AssertClean(Validate(response));
        var below = Validate(response, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a response unlessCondition", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlessCondition_a_non_condition_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { UnlessCondition = "sleepy" })), "validate.response-condition");
    }

    [Fact]
    public void Supersedes_a_response_of_this_revision_is_valid()
    {
        var other = new ResponseEffect { Id = "o", ResponseId = "other", Label = "Other", Phase = ResponsePhase.DamageReduction, Amount = "1" };
        var response = Reduction(r => r with { Supersedes = new(null, "other") });
        AssertClean(ContentValidator.Validate(Built(12, response, other), Catalog));
        var below = ContentValidator.Validate(Built(11, response, other), Catalog);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a superseding response", StringComparison.Ordinal));
    }

    [Fact]
    public void Supersedes_itself_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Supersedes = new(null, "ward") })), "validate.response-supersedes");
        AssertError(Validate(Reduction(r => r with { Supersedes = new(ContentId, "ward") })), "validate.response-supersedes");
    }

    [Fact]
    public void Supersedes_a_response_this_revision_does_not_define_is_refused()
    {
        AssertError(Validate(Reduction(r => r with { Supersedes = new(null, "missing") })), "validate.response-supersedes");
    }

    [Fact]
    public void Supersedes_a_response_of_content_that_is_not_installed_warns()
    {
        var report = Validate(Reduction(r => r with { Supersedes = new(Guid.Parse("6a7e0200-0000-4000-8000-0000000000ff"), "other") }));
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Code == "validate.response-supersedes");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-content-missing");
    }

    private static ResponseEffect Link(string id, string target, Guid? content = null) =>
        new() { Id = id, ResponseId = id, Label = id, Phase = ResponsePhase.DamageReduction, Amount = "1", Supersedes = new(content, target) };

    [Fact]
    public void Two_responses_that_supersede_each_other_are_refused()
    {
        var report = ContentValidator.Validate(Built(12, Link("a", "b"), Link("b", "a")), Catalog);

        Assert.Equal(2, report.Errors.Count(e => e.Code == "validate.response-supersedes"));
    }

    [Fact]
    public void A_three_response_loop_is_refused()
    {
        var report = ContentValidator.Validate(Built(12, Link("a", "b"), Link("b", "c"), Link("c", "a")), Catalog);

        Assert.Equal(3, report.Errors.Count(e => e.Code == "validate.response-supersedes"));
    }

    [Fact]
    public void A_chain_that_does_not_loop_is_valid()
    {
        var plain = new ResponseEffect { Id = "c", ResponseId = "c", Label = "c", Phase = ResponsePhase.DamageReduction, Amount = "1" };

        AssertClean(ContentValidator.Validate(Built(12, Link("a", "b"), Link("b", "c"), plain), Catalog));
    }

    [Fact]
    public void A_loop_through_content_already_installed_is_refused()
    {
        var other = Built(12, Link("far", "ward", ContentId)) with
        {
            ContentId = Guid.Parse("6a7e0200-0000-4000-8000-000000000002"), RevisionId = Guid.Parse("6a7e0200-0000-4000-8000-000000000102"),
        };
        var catalog = new InMemoryContentCatalog([Catalog.FindSource(Fixtures.Source2024)!], [other]);
        var mine = Built(12, Reduction(r => r with { Supersedes = new(other.ContentId, "far") }));

        AssertError(ContentValidator.Validate(mine, catalog), "validate.response-supersedes");
    }

    [Fact]
    public void A_loop_only_through_an_older_published_revision_is_valid()
    {
        var otherContent = Guid.Parse("6a7e0200-0000-4000-8000-000000000002");
        var older = Built(12, Link("far", "ward", ContentId)) with
        {
            ContentId = otherContent, RevisionId = Guid.Parse("6a7e0200-0000-4000-8000-000000000102"), Status = RevisionStatus.Published,
        };
        var far = new ResponseEffect { Id = "far", ResponseId = "far", Label = "far", Phase = ResponsePhase.DamageReduction, Amount = "1" };
        var newest = Built(12, far) with
        {
            ContentId = otherContent, RevisionId = Guid.Parse("6a7e0200-0000-4000-8000-000000000103"), Status = RevisionStatus.Published,
        };
        var catalog = new InMemoryContentCatalog([Catalog.FindSource(Fixtures.Source2024)!], [older, newest]);
        var mine = Built(12, Reduction(r => r with { Supersedes = new(otherContent, "far") }));

        // The older revision is insert-only, so the edge its author has since removed must not refuse this publish.
        Assert.DoesNotContain(ContentValidator.Validate(mine, catalog).Errors, e => e.Code == "validate.response-supersedes");
    }

    [Theory]
    [InlineData("1d4-5")]
    [InlineData("-1d6")]
    public void Dice_that_can_total_below_zero_are_refused(string dice)
    {
        AssertError(Validate(Reduction(r => r with { Dice = dice })), "validate.response-dice");
    }

    [Fact]
    public void Dice_with_a_constant_and_a_zero_minimum_are_valid()
    {
        AssertClean(Validate(Reduction(r => r with { Dice = "1d4-1" })));
        AssertClean(Validate(Reduction(r => r with { Dice = "2d6+3" })));
    }

    [Fact]
    public void Unreadable_v12_response_at_v12_is_malformed_and_names_the_rung()
    {
        const string json = "{\"contentId\":\"6a7e0200-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0200-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Ward\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"response\",\"id\":\"r\",\"responseId\":\"ward\",\"label\":\"Ward\",\"phase\":\"damageReduction\",\"amount\":\"3\",\"share\":\"most\"}]}";
        var report = ContentValidator.Validate(Properties.RevisionHashProperties.Read(json), Catalog);
        AssertError(report, "validate.effect-malformed");
        Assert.Equal(12, report.RequiredSchemaVersion);
    }
}
