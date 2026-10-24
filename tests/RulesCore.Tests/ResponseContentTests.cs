using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 7, Task 7.2 (D43, content schema v11): the <c>response</c> effect (damage-time responses) and the
/// <c>HP.MAX</c> formula identifier: the version gate that keeps older revisions byte for byte, validation, and the fixture
/// pack. Nothing here calculates a response or <c>HP.MAX</c>; later tasks read them.
/// </summary>
public class ResponseContentTests
{
    private static ValidationReport Validate(ContentRevision revision, IEnumerable<ContentRevision>? batch = null) =>
        ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog(), batch);

    private const string HeavyContent = "6f0dc000-0000-4000-8000-000000000061"; // a toggle `heavy` and no roll
    private const string BulwarkContent = "6f0dc000-0000-4000-8000-000000000070"; // the pool `grit`
    private const string AbsentContent = "6f0dc000-0000-4000-8000-0000000000aa"; // installed nowhere

    private const string Pool = """{"type":"resource","id":"p","resourceId":"grit","label":"Grit","maximum":"3"}""";
    private const string Surge = """{"type":"toggle","id":"t","toggleId":"surge","label":"Surge"}""";
    private const string Counter = """{"type":"roll","id":"c","rollId":"counter","label":"Counter","dice":"1d8"}""";

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_response_entry_stays_unknown(int version)
    {
        var json = """{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"5"}]}""";
        var revision = Fixtures.RevisionWithEffect(json, version);
        var effect = Assert.IsType<UnknownEffect>(revision.Effects.Single());

        Assert.Equal(json, Fixtures.EffectJson(effect));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(RevisionHashProperties.Hash(stored), RevisionHashProperties.Hash(RevisionHashProperties.Write(RevisionHashProperties.Read(stored))));
        Assert.Contains(json, stored);
    }

    [Fact]
    public void A_v11_response_entry_is_typed_and_round_trips()
    {
        var json = """{"type":"response","id":"r","responseId":"rally","label":"Rally","phase":"threshold","when":{"atOrBelow":"floor(HP.MAX / 4)"},"resourceId":"grit","resourceContent":"6f0dc000-0000-4000-8000-000000000070","cost":"1","toggle":{"contentId":"6f0dc000-0000-4000-8000-000000000074","toggleId":"surge"},"outcomes":[{"kind":"spend","resourceId":"grit","resourceContent":"6f0dc000-0000-4000-8000-000000000070","amount":"1"},{"kind":"regainHitPoints","amount":"5"},{"kind":"temporaryHitPoints","amount":"PB"},{"kind":"toggleOn","toggle":{"toggleId":"surge"}},{"kind":"offerRoll","rollId":"counter","rollContent":"6f0dc000-0000-4000-8000-000000000076"}]}""";
        var response = Assert.IsType<ResponseEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal("rally", response.ResponseId);
        Assert.Equal(ResponsePhase.Threshold, response.Phase);
        Assert.Equal(new ResponseWhen("floor(HP.MAX / 4)", null), response.When);
        Assert.Equal(new ToggleRef(Fixtures.SurgeForm.ContentId, "surge"), response.Toggle);
        Assert.Equal(
            [ResponseOutcomeKind.Spend, ResponseOutcomeKind.RegainHitPoints, ResponseOutcomeKind.TemporaryHitPoints, ResponseOutcomeKind.ToggleOn, ResponseOutcomeKind.OfferRoll],
            response.Outcomes!.Select(o => o.Kind));

        var written = Fixtures.EffectJson(response);
        Assert.Contains("\"kind\":\"regainHitPoints\"", written);
        Assert.Contains("\"kind\":\"temporaryHitPoints\"", written);
        Assert.Contains("\"phase\":\"threshold\"", written);
        Assert.DoesNotContain("null", written);
        Assert.Equal(written, Fixtures.EffectJson(Fixtures.RevisionWithEffect(written, 11).Effects.Single()));
    }

    [Theory]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"sideways","amount":"3"}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":" threshold","when":{"survived":true}}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold, damageReduction","when":{"survived":true}}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"sideways"}]}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"1"},{"kind":" spend","resourceId":"grit","amount":"1"}]}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend, regain","resourceId":"grit","amount":"1"}]}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"amount":"1"}]}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":"spend"}""")]
    [InlineData("""{"type":"response","id":"r","label":"Brace","phase":"damageReduction","amount":"3"}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","phase":"damageReduction","amount":"3"}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","amount":"3"}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","resourceContent":"not-an-id"}""")]
    public void A_bad_response_enum_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_response_in_a_revision_declaring_v10_requires_v11_when_built_in_code()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3"}""", 11) with { SchemaVersion = 10 };

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v11");
        Assert.Contains("damage responses", report.Errors.First(e => e.Code == "validate.requires-v11").Message);
    }

    // ---- HP.MAX ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void HP_MAX_is_a_formula_feature_of_its_own_that_no_revision_gets_by_default()
    {
        Assert.Equal(4, (int)FormulaFeatures.HitPointMaximum);
        Assert.Equal("HP.MAX", FormulaIdentifiers.HitPointMaximum);
        Assert.False(FormulaIdentifiers.IsKnown(FormulaIdentifiers.HitPointMaximum));
        Assert.DoesNotContain(FormulaIdentifiers.HitPointMaximum, FormulaIdentifiers.All);
        Assert.False(Formula.FeaturesFor(Fixtures.RevisionWithEffect("""{"type":"text","id":"x"}""", 11)).HasFlag(FormulaFeatures.HitPointMaximum));

        const FormulaFeatures everyOther = FormulaFeatures.Scales | FormulaFeatures.Values;
        Assert.False(Formula.TryParse("floor(HP.MAX / 4)", everyOther, out _, out var refused));
        Assert.Equal("formula.unknown-identifier", refused!.Code);
        Assert.False(Formula.TryParse("HP.MAX", out _, out _));

        Assert.True(Formula.TryParse("floor(HP.MAX / 4)", everyOther | FormulaFeatures.HitPointMaximum, out var formula, out _));
        Assert.Contains("HP.MAX", formula!.Identifiers);
        Assert.False(Formula.TryParse("HP.MAXIMUM", everyOther | FormulaFeatures.HitPointMaximum, out _, out _));
        Assert.False(Formula.TryParse("hp.max", everyOther | FormulaFeatures.HitPointMaximum, out _, out _));
    }

    [Theory]
    [InlineData("""{"type":"modifier","id":"r","operation":"bonus","target":"initiative","value":"HP.MAX"}""")]
    [InlineData("""{"type":"resource","id":"r","resourceId":"grit","label":"Grit","maximum":"HP.MAX"}""")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"HP.MAX"}""")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"1","resourceId":"grit","cost":"HP.MAX"}""")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend","resourceId":"grit","amount":"HP.MAX"}]}""")]
    public void HP_MAX_is_only_valid_in_a_threshold_when_so_anywhere_else_it_is_a_formula_error(string effects)
    {
        var report = Validate(Fixtures.RevisionWithEffect(effects, 11));

        Assert.Contains(report.Errors, e => e.Code == "validate.formula-invalid" && e.EffectId == "r");
    }

    [Fact]
    public void HP_MAX_is_valid_in_a_threshold_at_or_below()
    {
        var report = Validate(Fixtures.RevisionWithEffect("""{"type":"response","id":"r","responseId":"rally","label":"Rally","phase":"threshold","when":{"atOrBelow":"floor(HP.MAX / 4)"}}""", 11));

        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    // ---- validation -------------------------------------------------------------------------------------------------------

    [Theory]
    // validate.response-incomplete
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"","phase":"damageReduction","amount":"3"}""", "validate.response-incomplete")]
    [InlineData("""{"type":"response","id":"r","responseId":"  ","label":"Brace","phase":"damageReduction","amount":"3"}""", "validate.response-incomplete")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","cost":"1"}""", "validate.response-incomplete")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","resourceId":"ghost","cost":"1"}""", "validate.response-incomplete")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","resourceId":"grit","resourceContent":AbsentContent,"cost":"1"}""", "validate.response-incomplete")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","resourceId":"ghost","resourceContent":"6f0dc000-0000-4000-8000-000000000070","cost":"1"}""", "validate.response-incomplete")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","resourceId":"grit","cost":"1"}""", "validate.response-incomplete")]
    // validate.response-amount
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction"}""", "validate.response-amount")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"hitPointOutcome"}""", "validate.response-amount")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","amount":"3","when":{"survived":true}}""", "validate.response-amount")]
    // validate.response-when
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold"}""", "validate.response-when")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"damageReduction","amount":"3","when":{"survived":true}}""", "validate.response-when")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"hitPointOutcome","amount":"1","when":{"atOrBelow":"5"}}""", "validate.response-when")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"atOrBelow":"5","survived":true}}""", "validate.response-when")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{}}""", "validate.response-when")]
    [InlineData("""{"type":"response","id":"r","responseId":"brace","label":"Brace","phase":"threshold","when":{"survived":false}}""", "validate.response-when")]
    // validate.response-outcome: a kind missing its keys
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend","resourceId":"grit"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend","amount":"1"}]}""", "validate.response-outcome")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regain","resourceId":"grit"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"toggleOn"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"offerRoll"}]}""", "validate.response-outcome")]
    // validate.response-outcome: an unknown pool, toggle or roll
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend","resourceId":"ghost","amount":"1"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regain","resourceId":"grit","resourceContent":AbsentContent,"amount":"1"}]}""", "validate.response-outcome")]
    [InlineData(Surge + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"toggleOn","toggle":{"toggleId":"ghost"}}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"toggleOn","toggle":{"contentId":"6f0dc000-0000-4000-8000-000000000061","toggleId":"ghost"}}]}""", "validate.response-outcome")]
    [InlineData(Counter + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"offerRoll","rollId":"ghost"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"offerRoll","rollId":"counter","rollContent":AbsentContent}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"offerRoll","rollId":"counter","rollContent":"6f0dc000-0000-4000-8000-000000000061"}]}""", "validate.response-outcome")]
    // validate.response-outcome: one hit point outcome per event (R1)
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"5"},{"kind":"temporaryHitPoints","amount":"5"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"5"},{"kind":"regainHitPoints","amount":"5"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"hitPointOutcome","amount":"1","outcomes":[{"kind":"regainHitPoints","amount":"5"}]}""", "validate.response-outcome")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"hitPointOutcome","amount":"1","outcomes":[{"kind":"temporaryHitPoints","amount":"5"}]}""", "validate.response-outcome")]
    // validate.response-outcome-amount
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints"}]}""", "validate.response-outcome-amount")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"temporaryHitPoints"}]}""", "validate.response-outcome-amount")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"banana"}]}""", "validate.response-outcome-amount")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"temporaryHitPoints","amount":"HP.MAX"}]}""", "validate.response-outcome-amount")]
    // validate.response-duplicate
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1"},{"type":"response","id":"r2","responseId":"x","label":"Y","phase":"damageReduction","amount":"2"}""", "validate.response-duplicate")]
    // validate.while-toggle-unknown: the response's own toggle
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","toggle":{"toggleId":"ghost"}}""", "validate.while-toggle-unknown")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","toggle":{"contentId":"6f0dc000-0000-4000-8000-000000000061","toggleId":"ghost"}}""", "validate.while-toggle-unknown")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","toggle":{"toggleId":" "}}""", "validate.while-toggle-unknown")]
    // the formulas keep their usual code
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"banana"}""", "validate.formula-invalid")]
    [InlineData("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"atOrBelow":"banana"}}""", "validate.formula-invalid")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","resourceId":"grit","cost":"banana"}""", "validate.formula-invalid")]
    [InlineData(Pool + "," + """{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"spend","resourceId":"grit","amount":"banana"}]}""", "validate.formula-invalid")]
    public void Response_mistakes_are_errors(string effects, string code)
    {
        var revision = Fixtures.RevisionWithEffect(effects.Replace("AbsentContent", $"\"{AbsentContent}\"", StringComparison.Ordinal), 11);
        Assert.NotEmpty(revision.Effects.OfType<ResponseEffect>());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == code && e.EffectId is "r" or "r2");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Every_response_mistake_names_ids_and_counts_never_user_text()
    {
        var report = Validate(Fixtures.RevisionWithEffect("""{"type":"response","id":"r","responseId":"brace","label":"Secret label","phase":"threshold","amount":"3","when":{"atOrBelow":"5","survived":true},"outcomes":[{"kind":"regainHitPoints","amount":"5"},{"kind":"temporaryHitPoints","amount":"5"}]}""", 11));

        Assert.Contains(report.Errors, e => e.Code.StartsWith("validate.response-", StringComparison.Ordinal));
        Assert.All(report.Errors.Where(e => e.Code.StartsWith("validate.response-", StringComparison.Ordinal)), e => Assert.DoesNotContain("Secret label", e.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_response_of_each_phase_with_every_outcome_kind_is_clean()
    {
        var report = Validate(Fixtures.RevisionWithEffect(
            Pool + "," + Surge + "," + Counter + ","
            + """{"type":"response","id":"a","responseId":"a","label":"A","phase":"damageReduction","amount":"3 + CON.MOD","resourceId":"grit","cost":"1"},"""
            + """{"type":"response","id":"b","responseId":"b","label":"B","phase":"hitPointOutcome","amount":"1","toggle":{"toggleId":"surge"}},"""
            + """{"type":"response","id":"c2","responseId":"c","label":"C","phase":"threshold","when":{"atOrBelow":"floor(HP.MAX / 2)"},"outcomes":[{"kind":"spend","resourceId":"grit","amount":"1"},{"kind":"regain","resourceId":"grit","amount":"PB"},{"kind":"toggleOn","toggle":{"toggleId":"surge"}},{"kind":"offerRoll","rollId":"counter"},{"kind":"regainHitPoints","amount":"PB + CON.MOD"}]},"""
            + """{"type":"response","id":"d","responseId":"d","label":"D","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"temporaryHitPoints","amount":"5"}]}""", 11));

        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_response_that_names_content_which_is_not_installed_warns_the_way_a_grant_whileToggle_does()
    {
        var grant = Validate(Fixtures.RevisionWithEffect($$$"""{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"contentId":"{{{AbsentContent}}}","toggleId":"surge"}}""", 11));
        var response = Validate(Fixtures.RevisionWithEffect($$$"""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","toggle":{"contentId":"{{{AbsentContent}}}","toggleId":"surge"}}""", 11));

        Assert.Empty(grant.Errors);
        Assert.Empty(response.Errors);
        Assert.Contains(grant.Warnings, w => w.Code == "validate.toggle-content-missing");
        Assert.Contains(response.Warnings, w => w.Code == "validate.toggle-content-missing" && w.EffectId == "r");
    }

    [Fact]
    public void A_response_may_spend_a_pool_or_follow_a_toggle_of_another_content()
    {
        var report = Validate(Fixtures.RevisionWithEffect(
            $$$"""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"resourceId":"grit","resourceContent":"{{{BulwarkContent}}}","cost":"1","toggle":{"contentId":"{{{HeavyContent}}}","toggleId":"heavy"},"outcomes":[{"kind":"regain","resourceId":"grit","resourceContent":"{{{BulwarkContent}}}","amount":"1"},{"kind":"toggleOn","toggle":{"contentId":"{{{HeavyContent}}}","toggleId":"heavy"}}]}""", 11));

        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_pool_is_found_when_any_revision_of_its_content_defines_it()
    {
        // Two revisions of one content: the older one defines `grit`, the newer one does not. The reference names the content, not a revision.
        var older = Fixtures.RevisionWithEffect(Pool, 11) with { ContentId = Guid.Parse(AbsentContent), RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000a1") };
        var newer = older with { RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000a2"), Effects = [] };
        var response = Fixtures.RevisionWithEffect($$"""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1","resourceId":"grit","resourceContent":"{{AbsentContent}}","cost":"1"}""", 11);

        Assert.Empty(Validate(response, [older, newer]).Errors);
        Assert.Empty(Validate(response, [newer, older]).Errors);
    }

    [Fact]
    public void Twenty_responses_are_allowed_and_twenty_one_in_one_revision_are_refused()
    {
        string Responses(int count) => string.Join(",", Enumerable.Range(0, count).Select(i =>
            $$"""{"type":"response","id":"r{{i}}","responseId":"x{{i}}","label":"X","phase":"damageReduction","amount":"1"}"""));

        Assert.Empty(Validate(Fixtures.RevisionWithEffect(Responses(ContentValidator.MaxResponsesPerRevision), 11)).Errors);

        var report = Validate(Fixtures.RevisionWithEffect(Responses(ContentValidator.MaxResponsesPerRevision + 1), 11));

        Assert.Equal(20, ContentValidator.MaxResponsesPerRevision);
        Assert.Contains(report.Errors, e => e.Code == "validate.response-too-many");
    }

    [Fact]
    public void Nine_outcomes_on_one_response_are_refused()
    {
        const string spend = """{"kind":"spend","resourceId":"grit","amount":"1"}""";
        string Response(int outcomes)
        {
            var list = string.Join(",", Enumerable.Repeat(spend, outcomes));
            return Pool + "," + $$"""{"type":"response","id":"r","responseId":"x","label":"X","phase":"threshold","when":{"survived":true},"outcomes":[{{list}}]}""";
        }

        Assert.Empty(Validate(Fixtures.RevisionWithEffect(Response(ContentValidator.MaxOutcomesPerResponse), 11)).Errors);

        var report = Validate(Fixtures.RevisionWithEffect(Response(ContentValidator.MaxOutcomesPerResponse + 1), 11));

        Assert.Equal(8, ContentValidator.MaxOutcomesPerResponse);
        Assert.Contains(report.Errors, e => e.Code == "validate.response-too-many" && e.EffectId == "r");
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var plain = Validate(Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""", 10));
        Assert.Empty(plain.Errors);
        Assert.True(plain.RequiredSchemaVersion < 10, $"required v{plain.RequiredSchemaVersion}");

        var formula = Validate(Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"CON.MOD"}""", 10));
        Assert.Empty(formula.Errors);
        Assert.True(formula.RequiredSchemaVersion < 10, $"required v{formula.RequiredSchemaVersion}");

        var response = Validate(Fixtures.RevisionWithEffect("""{"type":"response","id":"r","responseId":"x","label":"X","phase":"damageReduction","amount":"1"}""", 11));
        Assert.Empty(response.Errors);
        Assert.Equal(11, response.RequiredSchemaVersion);
    }

    // ---- the fixture pack ------------------------------------------------------------------------------------------------

    private static ContentPack Pack => Fixtures.ResponsesPack();

    [Fact]
    public void The_responses_pack_validates_clean_and_types_every_response()
    {
        Assert.Equal(8, Pack.Revisions.Count);
        foreach (var revision in Pack.Revisions)
        {
            var report = Validate(revision, Pack.Revisions);
            Assert.Empty(report.Errors);
            Assert.Empty(report.Warnings);
            Assert.DoesNotContain(revision.Effects, e => e is UnknownEffect);
            if (revision.Effects.Any(e => e is ResponseEffect or ToggleGroupEffect))
                Assert.Equal(11, report.RequiredSchemaVersion);
        }

        var catalog = Fixtures.PlayMechanicsCatalog();
        ResponseEffect Only(ContentReference r) => catalog.FindRevision(r)!.Effects.OfType<ResponseEffect>().Single();
        var grit = Fixtures.Bulwark.ContentId;
        var surge = new ToggleRef(Fixtures.SurgeForm.ContentId, "surge");

        var brace = Only(Fixtures.BraceResponse);
        Assert.Equal("Fixture Brace Response", catalog.FindRevision(Fixtures.BraceResponse)!.Name);
        Assert.Equal((ResponsePhase.DamageReduction, "3 + CON.MOD", "grit", grit, "1"), (brace.Phase, brace.Amount, brace.ResourceId, brace.ResourceContent, brace.Cost));
        Assert.Null(brace.When);
        Assert.Null(brace.Outcomes);

        var stand = Only(Fixtures.LastStand);
        Assert.Equal((ResponsePhase.HitPointOutcome, "1", "stand", (Guid?)null, "1"), (stand.Phase, stand.Amount, stand.ResourceId, stand.ResourceContent, stand.Cost));
        Assert.Null(stand.Toggle);

        var endure = Only(Fixtures.Endure);
        Assert.Equal(ResponsePhase.HitPointOutcome, endure.Phase);
        Assert.Equal(surge, endure.Toggle);

        var rally = Only(Fixtures.Rally);
        Assert.Equal(ResponsePhase.Threshold, rally.Phase);
        Assert.Equal(new ResponseWhen("floor(HP.MAX / 4)", null), rally.When);
        Assert.Null(rally.Amount);
        Assert.Equal("rally", rally.ResourceId);
        Assert.Collection(
            rally.Outcomes!,
            o => Assert.Equal(new ResponseOutcome(ResponseOutcomeKind.ToggleOn, null, null, null, surge, null, null), o),
            o => Assert.Equal(new ResponseOutcome(ResponseOutcomeKind.Regain, "grit", grit, "1", null, null, null), o));

        var rebound = Only(Fixtures.Rebound);
        Assert.Equal(ResponsePhase.Threshold, rebound.Phase);
        Assert.Equal(new ResponseWhen(null, true), rebound.When);
        Assert.Equal(new ResponseOutcome(ResponseOutcomeKind.OfferRoll, null, null, null, null, "counter", null), Assert.Single(rebound.Outcomes!));
        Assert.Contains(catalog.FindRevision(Fixtures.Rebound)!.Effects.OfType<RollEffect>(), r => r is { RollId: "counter", Dice: "1d8" });

        var surgeRevision = catalog.FindRevision(Fixtures.SurgeForm)!;
        Assert.Equal("forms", surgeRevision.Effects.OfType<ToggleEffect>().Single().Group);
        Assert.Equal("forms", catalog.FindRevision(Fixtures.GuardForm)!.Effects.OfType<ToggleEffect>().Single().Group);
        Assert.Equal(6, catalog.FindRevision(Fixtures.Bulwark)!.Effects.OfType<GrantEffect>().Count());
    }

    [Fact]
    public void The_character_has_68_hit_points_and_the_class_grants_the_features()
    {
        var character = Fixtures.Load("pm-bulwark-l8.json");
        Assert.Equal(8, character.Level);
        Assert.Equal(9, character.SchemaVersion); // the lowest it needs: damage defences are v9, and it has no encounter
        Assert.Equal([new DamageDefence("fire", DamageDefenceKind.Resistance)], character.DamageDefences);
        Assert.Equal(
            [Fixtures.Bulwark, Fixtures.BraceResponse, Fixtures.LastStand, Fixtures.Endure, Fixtures.SurgeForm, Fixtures.Rally, Fixtures.Rebound, Fixtures.GuardForm],
            character.Pins);

        var sheet = CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

        Assert.Equal(68, sheet.Field(FieldIds.HitPoints).Value);
        Assert.Equal(14, sheet.Field(FieldIds.Score(Ability.Con)).Value);
        Assert.Equal(68, sheet.HitPoints!.Maximum);
        Assert.Equal(5, sheet.Resources!.Single(r => r.ResourceId == "grit").Maximum); // PB 3 + 2
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("effect.", StringComparison.Ordinal) && d.Code != "effect.assisted");
    }
}
