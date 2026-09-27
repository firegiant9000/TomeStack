namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M1 item 4 (SPEC C-01): a character's selections for choice effects. Counts are enforced, options must be valid and
/// usable under the character's rules family, unresolved choices are flagged, and chosen content joins the active set
/// with a "chosen from" trace. Original fixtures: "Fixture Warden" (skills 2 of 3; a path at level 3) and
/// "Fixture Crossroads" (a background whose ability increase is chosen).
/// </summary>
public class ChoiceTests
{
    private static Character Warden(int level, params ChoiceSelection[] choices) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [], Classes = [new(Fixtures.Warden, level)], Level = level, Choices = choices };

    private static ChoiceSelection Skills(params ContentReference[] selected) => new(Fixtures.Warden, "warden-skills", selected);

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.M1Catalog());

    /// <summary>An original test revision usable under both families, from the M1 shared fixture source.</summary>
    internal static ContentRevision Homebrew(int n, ContentKind kind, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"5f1dc000-0000-4000-8000-0000000001{n:D2}"),
        RevisionId = Guid.Parse($"5f1de000-0000-4000-8000-0000000001{n:D2}"),
        Kind = kind,
        Name = $"Fixture Homebrew {n}",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("5f0d5001-0000-4000-8000-000000000001")),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    /// <summary>The M0 and M1 fixture catalogs plus <paramref name="extra"/>.</summary>
    internal static CharacterSheet SheetWith(Character character, params ContentRevision[] extra)
    {
        var (m0, m1) = (Fixtures.Pack(), Fixtures.M1Pack());
        return CharacterCalculator.Calculate(character, new InMemoryContentCatalog([.. m0.Sources, .. m1.Sources], [.. m0.Revisions, .. m1.Revisions, .. extra]));
    }

    [Fact]
    public void Unresolved_choices_are_flagged_and_choices_above_the_class_level_are_not_offered_yet()
    {
        var sheet = Sheet(Warden(1));

        var skills = Assert.Single(sheet.Choices!);
        Assert.Equal(("warden-skills", 2, false), (skills.ChoiceId, skills.Count, skills.Resolved));
        Assert.Equal(("Fixture Warden", "Choose two skills."), (skills.SourceName, skills.Text));
        Assert.Equal([Fixtures.WardenAthletics, Fixtures.WardenSurvival, Fixtures.WardenNature], skills.Options);
        var flag = Assert.Single(sheet.Diagnostics, d => d.Code == "choice.unresolved");
        Assert.Equal((Fixtures.Warden, "warden-skills"), (flag.Content, flag.EffectId));

        Assert.Contains(Sheet(Warden(3)).Choices!, c => c.ChoiceId == "warden-path" && !c.Resolved);
    }

    [Fact]
    public void Chosen_content_is_active_and_its_trace_says_where_it_was_chosen_from()
    {
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival)));

        Assert.True(Assert.Single(sheet.Choices!).Resolved);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("choice.", StringComparison.Ordinal));
        var pb = sheet.Field(FieldIds.ProficiencyBonus).Value;
        var athletics = sheet.Field(FieldIds.Skill("athletics"));
        Assert.Equal(0 + pb, athletics.Value);
        var step = athletics.Trace[^1];
        Assert.Equal(Fixtures.WardenAthletics, step.Origin.Content);
        Assert.Contains("chosen from class 'Fixture Warden'", step.Description, StringComparison.Ordinal);
        Assert.Equal(0, sheet.Field(FieldIds.Skill("nature")).Value); // Int 10: +0, and Nature was not chosen
    }

    [Fact]
    public void A_chosen_subclass_belongs_to_its_class_and_its_level_features_follow_the_class_level()
    {
        var path = new ChoiceSelection(Fixtures.Warden, "warden-path", [Fixtures.PathOfThorns]);
        var at3 = Sheet(Warden(3, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival), path));

        Assert.All(at3.Choices!, c => Assert.True(c.Resolved));
        // Warden Guard: 10 + Dex 2 + Wis 1 = 13; Thorns adds floor(CLASS_LEVEL / 3) = 1 at Warden level 3.
        var ac = at3.Field(FieldIds.ArmorClass);
        Assert.Equal(13 + 1, ac.Value);
        var thorns = Assert.Single(ac.Trace, t => t.Origin.Content == Fixtures.Thorns);
        Assert.Contains("granted by subclass 'Fixture Path of Thorns'", thorns.Description, StringComparison.Ordinal);
        Assert.Equal([new TraceInput(FormulaIdentifiers.ClassLevel, 3)], thorns.Inputs);

        // The same stored selection at level 2 is not offered yet, so it is reported, not applied.
        var at2 = Sheet(Warden(2, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival), path));
        Assert.Contains(at2.Diagnostics, d => d.Code == "choice.orphaned" && d.Content == Fixtures.Warden);
        Assert.Equal(13, at2.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void The_count_is_enforced()
    {
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival, Fixtures.WardenNature)));

        var choice = Assert.Single(sheet.Choices!);
        Assert.Equal([Fixtures.WardenAthletics, Fixtures.WardenSurvival], choice.Selected);
        Assert.Equal(Fixtures.WardenNature, Assert.Single(sheet.Diagnostics, d => d.Code == "choice.too-many").Content);
        Assert.Equal(0, sheet.Field(FieldIds.Skill("nature")).Value);
    }

    [Fact]
    public void Only_listed_options_can_be_chosen()
    {
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.Hardy)));

        Assert.Equal(Fixtures.Hardy, Assert.Single(sheet.Diagnostics, d => d.Code == "choice.invalid-option").Content);
        Assert.False(Assert.Single(sheet.Choices!).Resolved);
        Assert.DoesNotContain(sheet.Field(FieldIds.HitPoints).Trace, t => t.Origin.Content == Fixtures.Hardy);
    }

    [Fact]
    public void An_option_for_another_rules_family_is_refused_and_leaves_the_choice_unresolved()
    {
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [Fixtures.Crossroads],
            Choices = [new(Fixtures.Crossroads, "crossroads-ability", [Fixtures.CrossroadsWis2014])],
        };

        var sheet = Sheet(character);

        var refused = Assert.Single(sheet.Diagnostics, d => d.Code == "content.rules-family-mismatch");
        Assert.StartsWith("Chosen from 'Fixture Crossroads'", refused.Message, StringComparison.Ordinal);
        Assert.False(Assert.Single(sheet.Choices!).Resolved);
        Assert.Equal(13, sheet.Field(FieldIds.Score(Ability.Wis)).Value);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd521, 11, false)]
    [InlineData(RulesFamilies.Srd51, 10, true)]
    public void An_ability_option_chosen_from_a_background_follows_the_background_policy(string family, int strength, bool blocked)
    {
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            RulesFamily = family,
            Pins = [Fixtures.Crossroads],
            Choices = [new(Fixtures.Crossroads, "crossroads-ability", [Fixtures.CrossroadsStr])],
        };

        var str = Sheet(character).Field(FieldIds.Score(Ability.Str));

        Assert.Equal(strength, str.Value);
        var warning = str.Warnings.SingleOrDefault(w => w.Code == "policy.ability-increase-source");
        Assert.Equal(blocked, warning is not null);
        if (blocked)
            Assert.Contains("from background 'Fixture Crossroads'", warning!.Message, StringComparison.Ordinal);
        else
            Assert.Contains("chosen from background 'Fixture Crossroads'", str.Trace[^1].Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51, false)]
    [InlineData(RulesFamilies.Srd521, true)]
    public void A_feat_offered_by_a_background_choice_follows_the_background_feat_policy(string family, bool applies)
    {
        var background = Homebrew(1, ContentKind.Background, new ChoiceEffect { Id = "feat", ChoiceId = "feat", Options = [Fixtures.Watchful] });
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            RulesFamily = family,
            Pins = [background.Reference],
            Choices = [new(background.Reference, "feat", [Fixtures.Watchful])],
        };

        var sheet = SheetWith(character, background);

        Assert.Equal(applies, sheet.Field(FieldIds.Initiative).Trace.Any(t => t.Origin.Content == Fixtures.Watchful));
        Assert.Equal(applies, Assert.Single(sheet.Choices!).Resolved);
        var refused = sheet.Diagnostics.SingleOrDefault(d => d.Code == "policy.background-feat");
        Assert.Equal(!applies, refused is not null);
        if (!applies)
            Assert.Contains("'Fixture Homebrew 1' (background) cannot offer a feat under SRD 5.1", refused!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_feat_granted_by_a_feature_chosen_from_a_background_is_still_refused_under_srd_5_1()
    {
        var feature = Homebrew(2, ContentKind.Feature, new GrantEffect { Id = "feat", Grant = GrantKind.Content, Content = Fixtures.Watchful });
        var background = Homebrew(3, ContentKind.Background, new ChoiceEffect { Id = "pick", ChoiceId = "pick", Options = [feature.Reference] });
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            RulesFamily = RulesFamilies.Srd51,
            Pins = [background.Reference],
            Choices = [new(background.Reference, "pick", [feature.Reference])],
        };

        var sheet = SheetWith(character, feature, background);

        Assert.DoesNotContain(Fixtures.Watchful, sheet.Active!);
        Assert.Contains("(feature from a background) cannot grant a feat", Assert.Single(sheet.Diagnostics, d => d.Code == "policy.background-feat").Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51, 0)]
    [InlineData(RulesFamilies.Srd521, 2)]
    public void An_ability_increase_several_features_away_from_a_background_still_counts_as_the_background(string family, int increase)
    {
        // Background grants a feature; that feature offers a choice; the chosen feature raises Strength.
        var boost = Homebrew(4, ContentKind.Feature, new ModifierEffect { Id = "str", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Str), Value = "2" });
        var offer = Homebrew(5, ContentKind.Feature, new ChoiceEffect { Id = "pick", ChoiceId = "pick", Options = [boost.Reference] });
        var background = Homebrew(6, ContentKind.Background, new GrantEffect { Id = "offer", Grant = GrantKind.Content, Content = offer.Reference });
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            RulesFamily = family,
            Pins = [background.Reference],
            Choices = [new(offer.Reference, "pick", [boost.Reference])],
        };

        var str = SheetWith(character, boost, offer, background).Field(FieldIds.Score(Ability.Str));

        Assert.Equal(10 + increase, str.Value);
        var warning = str.Warnings.SingleOrDefault(w => w.Code == "policy.ability-increase-source");
        Assert.Equal(increase == 0, warning is not null);
        if (warning is not null)
            Assert.Contains("from background content via feature 'Fixture Homebrew 5'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selections_that_point_back_at_each_other_cannot_loop()
    {
        var catalog = Fixtures.M1Catalog();
        var source = catalog.FindSource(Guid.Parse("5f0d5001-0000-4000-8000-000000000001"))!;
        ContentRevision Offering(int n, int other) => new()
        {
            ContentId = Guid.Parse($"5f1dc000-0000-4000-8000-0000000000{n}"),
            RevisionId = Guid.Parse($"5f1de000-0000-4000-8000-0000000000{n}"),
            Kind = ContentKind.Feature,
            Name = $"Fixture Loop {n}",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id),
            Status = RevisionStatus.Published,
            Effects =
            [
                new ChoiceEffect
                {
                    Id = "pick", ChoiceId = "pick",
                    Options = [new(Guid.Parse($"5f1dc000-0000-4000-8000-0000000000{other}"), Guid.Parse($"5f1de000-0000-4000-8000-0000000000{other}"))],
                },
                new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
            ],
        };
        var (a, b) = (Offering(91, 92), Offering(92, 91));
        var looped = new InMemoryContentCatalog([source, .. Fixtures.Pack().Sources], [a, b]);
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [a.Reference],
            Choices = [new(a.Reference, "pick", [b.Reference]), new(b.Reference, "pick", [a.Reference])],
        };

        var sheet = CharacterCalculator.Calculate(character, looped);

        Assert.Equal(2 + 1 + 1, sheet.Field(FieldIds.Initiative).Value); // each applies once
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved));
    }

    [Fact]
    public void Recorded_choices_are_validated_and_count_as_references()
    {
        var character = Warden(1, Skills(Fixtures.WardenAthletics), Skills(Fixtures.WardenSurvival));

        Assert.Contains(character.Validate(), d => d.Code == "character.choice-duplicate");
        Assert.Contains((Warden(1) with { Choices = [new(Fixtures.Warden, " ", [])] }).Validate(), d => d.Code == "character.choice-id-required");
        Assert.Contains(Fixtures.WardenAthletics, Warden(1, Skills(Fixtures.WardenAthletics)).AllReferences());
    }
}
