namespace TomeStack.RulesCore.Tests;

/// <summary>M1 item 3: schema, reference, formula and cycle problems are reported for a revision before it is published.</summary>
public class ContentValidatorTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static ContentRevision Draft(params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("5f1dc000-0000-4000-8000-0000000000e1"),
        RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000e1"),
        Kind = ContentKind.Feat,
        Name = "Fixture Draft",
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("5f0d5001-0000-4000-8000-000000000001"), new PageRef(30)),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static IEnumerable<string> Errors(ContentRevision revision) => ContentValidator.Validate(revision, Catalog).Errors.Select(e => e.Code);

    public static TheoryData<string> FixtureRevisions() =>
        [.. Fixtures.Pack().Revisions.Concat(Fixtures.M1Pack().Revisions).Select(r => r.Name + "|" + r.RevisionId)];

    [Theory]
    [MemberData(nameof(FixtureRevisions))]
    public void Every_fixture_revision_validates_without_errors(string key)
    {
        var id = Guid.Parse(key.Split('|')[1]);
        var revision = Fixtures.Pack().Revisions.Concat(Fixtures.M1Pack().Revisions).Single(r => r.RevisionId == id);

        var report = ContentValidator.Validate(revision, Catalog);

        Assert.True(report.CanPublish, string.Join("; ", report.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void A_clean_draft_can_be_published_and_an_unsupported_effect_is_only_a_warning()
    {
        var report = ContentValidator.Validate(Draft(new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB" }), Catalog);
        Assert.True(report.CanPublish);

        var unknown = Fixtures.Pack().Revisions.Single(r => r.Effects.OfType<UnknownEffect>().Any());
        var withUnknown = ContentValidator.Validate(unknown, Catalog);
        Assert.True(withUnknown.CanPublish);
        Assert.Contains(withUnknown.Warnings, w => w.Code == "validate.effect-unsupported");
    }

    [Fact]
    public void Content_cannot_target_passives_or_speed_until_a_schema_version_allows_it()
    {
        // D24: these are calculated and overridable on the sheet, but no content-schema version carries them as targets.
        var passive = ContentValidator.Validate(Draft(new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" }), Catalog);
        Assert.Contains(passive.Errors, e => e.Code == "validate.unknown-target" && e.Message.Contains("cannot be targeted by content yet"));
        Assert.False(passive.CanPublish);
        var speed = ContentValidator.Validate(Draft(new RestrictionEffect { Id = "fast", Field = FieldIds.Speed, Minimum = 30 }), Catalog);
        Assert.Contains(speed.Errors, e => e.Code == "validate.unknown-target" && e.Message.Contains("cannot be targeted by content yet"));
    }

    [Fact]
    public void Schema_problems_are_reported()
    {
        Assert.Contains("validate.name-required", Errors(Draft() with { Name = " " }));
        Assert.Contains("validate.rules-family-required", Errors(Draft() with { RulesFamilies = [] }));
        Assert.Contains("validate.rules-family-unknown", Errors(Draft() with { RulesFamilies = ["5e"] }));
        Assert.Contains("validate.page-invalid", Errors(Draft() with { Provenance = new(Guid.Parse("5f0d5001-0000-4000-8000-000000000001"), new PageRef(5, 3)) }));
        Assert.Contains("validate.effect-id", Errors(Draft(
            new ModifierEffect { Id = "a", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
            new ModifierEffect { Id = "a", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" })));
        Assert.Contains("validate.unknown-target", Errors(Draft(new ModifierEffect { Id = "a", Operation = ModifierOperation.Bonus, Target = "speed", Value = "1" })));
        Assert.Contains("validate.unknown-target", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = FieldIds.Initiative })));
        Assert.Contains("validate.stack-group-missing", Errors(Draft(new ModifierEffect { Id = "a", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1", Stacking = StackingRule.HighestInGroup })));
        Assert.Contains("validate.level", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = Fixtures.Watchful, Level = 21 })));
        Assert.Contains("validate.choice-count", Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c", Count = 2, Options = [Fixtures.Watchful] })));
        // Content v9 (M5 slice 1b): a choice with no options of its own offers only content that extends it (a new
        // homebrew class's subclass choice). A warning, and v9: older builds refuse it by version.
        var noOptions = ContentValidator.Validate(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c" }), Catalog);
        Assert.Empty(noOptions.Errors);
        Assert.Contains(noOptions.Warnings, w => w.Code == "validate.choice-options-none");
        Assert.Equal(9, noOptions.RequiredSchemaVersion);
        // Without declared options the count keeps its upper bound (the v9 schema's maximum of 20).
        Assert.Empty(Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c", Count = ContentValidator.MaxChoiceCountWithoutOptions })));
        Assert.Contains("validate.choice-count", Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c", Count = ContentValidator.MaxChoiceCountWithoutOptions + 1 })));
        Assert.Contains("validate.choice-count", Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c", Count = 50, Options = [] })));
        Assert.Contains("validate.requires-v9", Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c" }) with { SchemaVersion = 8 }));
        Assert.Contains("validate.hit-die", Errors(Draft(new HitDieEffect { Id = "d", Die = 20 })));
        Assert.Contains("validate.dice-invalid", Errors(Draft(new RollEffect { Id = "r", RollId = "r", Label = "Roll", Dice = "1d20+d" })));
        Assert.Contains("validate.requires-v3", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = Fixtures.Watchful, Level = 3 }) with { SchemaVersion = 2 }));
    }

    [Fact]
    public void Reference_problems_are_reported()
    {
        var missing = new ContentReference(Guid.NewGuid(), Guid.NewGuid());

        Assert.Contains("validate.source-missing", Errors(Draft() with { Provenance = new(Guid.NewGuid()) }));
        Assert.Contains("validate.reference-missing", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = missing })));
        Assert.Contains("validate.reference-missing", Errors(Draft(new ChoiceEffect { Id = "c", ChoiceId = "c", Options = [missing] })));
        Assert.Contains("validate.reference-family", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = Fixtures.KeenSenses2014 })));
        Assert.Contains("validate.self-reference", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = new(Guid.Parse("5f1dc000-0000-4000-8000-0000000000e1"), Guid.NewGuid()) })));
        Assert.Contains("validate.grant-content-missing", Errors(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content })));

        // A revision validated in the same batch counts as present.
        var other = Draft() with { ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Name = "Fixture Other" };
        var report = ContentValidator.Validate(Draft(new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = other.Reference }), Catalog, [other]);
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.reference-missing");
        Assert.Contains(report.Warnings, w => w.Code == "validate.reference-unpublished");
    }

    [Fact]
    public void Formula_problems_are_reported_for_every_formula_field()
    {
        Assert.Contains("validate.formula-invalid", Errors(Draft(new ModifierEffect { Id = "a", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB +" })));
        Assert.Contains("validate.formula-invalid", Errors(Draft(new ResourceEffect { Id = "r", ResourceId = "r", Label = "Uses", Maximum = "eval(1)" })));
        Assert.Contains("validate.formula-invalid", Errors(Draft(
            new ResourceEffect { Id = "r", ResourceId = "r", Label = "Uses", Maximum = "PB" },
            new RecoveryEffect { Id = "rec", ResourceId = "r", On = RestPeriod.ShortRest, Amount = "1 / 0 +" })));
        Assert.DoesNotContain("validate.formula-invalid", Errors(Draft(
            new ResourceEffect { Id = "r", ResourceId = "r", Label = "Uses", Maximum = "PB" },
            new RecoveryEffect { Id = "rec", ResourceId = "r", On = RestPeriod.LongRest, Amount = "all" })));
    }

    [Fact]
    public void Dependency_cycles_are_reported()
    {
        var report = ContentValidator.Validate(Draft(
            new ModifierEffect { Id = "loop", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Dex), Value = "DEX.MOD" }), Catalog);

        var cycle = Assert.Single(report.Errors, e => e.Code == "effect.dependency-cycle");
        Assert.Equal("loop", cycle.EffectId);
        Assert.False(report.CanPublish);
    }
}
