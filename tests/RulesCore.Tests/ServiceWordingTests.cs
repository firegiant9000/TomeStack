using System.Text.RegularExpressions;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Wave 3, Task 2b (D99): the messages a player reads on the sheet use plain words, name content by its display name and never
/// print a GUID or an author id. <see cref="AssertPlain"/> is the shared check; later tasks extend this class.
/// </summary>
public partial class ServiceWordingTests
{
    private static readonly string[] BannedWords = ["revision", "rules family", "schema", "package", "candidate"];

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    /// <summary>A rules-family id such as "srd-5.1" or "srd-9": a player never reads one unless the author typed an id the app does not know.</summary>
    [GeneratedRegex(@"srd-\d", RegexOptions.IgnoreCase)]
    private static partial Regex FamilyIdPattern();

    /// <summary>The reusable wording check: no GUID, none of the banned words, no rules-family id, none of <paramref name="alsoNot"/> (ids), and every <paramref name="names"/> present.</summary>
    internal static void AssertPlain(string message, string[] names, params string[] alsoNot) => AssertPlainKeeping(message, [], names, alsoNot);

    /// <summary>As <see cref="AssertPlain"/>, but the unknown rules ids in <paramref name="keptIds"/> are expected to stay in the message (the owner ruling: a typo must stay findable).</summary>
    internal static void AssertPlainKeeping(string message, string[] keptIds, string[] names, params string[] alsoNot)
    {
        Assert.DoesNotMatch(GuidPattern(), message);
        var withoutKept = keptIds.Aggregate(message, (m, id) => m.Replace(id, "", StringComparison.Ordinal));
        Assert.DoesNotMatch(FamilyIdPattern(), withoutKept);
        foreach (var id in keptIds)
            Assert.Contains(id, message, StringComparison.Ordinal);
        foreach (var word in BannedWords)
            Assert.DoesNotContain(word, message, StringComparison.OrdinalIgnoreCase);
        foreach (var id in alsoNot)
            Assert.DoesNotContain(id, message, StringComparison.Ordinal);
        foreach (var name in names)
            Assert.Contains(name, message, StringComparison.Ordinal);
    }

    private static Character Warden(int level, params ChoiceSelection[] choices) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [], Classes = [new(Fixtures.Warden, level)], Level = level, Choices = choices };

    private static ChoiceSelection Skills(params ContentReference[] selected) => new(Fixtures.Warden, "warden-skills", selected);

    private static string NameOf(ContentReference reference) => Fixtures.M1Catalog().FindRevision(reference)!.Name;

    private static string Message(CharacterSheet sheet, string code) => Assert.Single(sheet.Diagnostics, d => d.Code == code).Message;

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.M1Catalog());

    [Fact]
    public void Too_many_picks_name_the_content_and_the_extra_pick()
    {
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival, Fixtures.WardenNature)));

        AssertPlain(Message(sheet, "choice.too-many"), ["Fixture Warden", NameOf(Fixtures.WardenNature), "one too many"], "warden-skills");
    }

    [Fact]
    public void An_option_that_is_not_offered_is_named_not_numbered()
    {
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.Hardy)));

        AssertPlain(Message(sheet, "choice.invalid-option"), ["Fixture Warden", NameOf(Fixtures.Hardy), "is not one of its options"], "warden-skills");
    }

    [Fact]
    public void An_option_missing_from_the_library_reads_as_not_available()
    {
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, gone)));

        AssertPlain(Message(sheet, "choice.invalid-option"), ["an entry that is not available"], "warden-skills", gone.RevisionId.ToString());
    }

    [Fact]
    public void An_unresolved_choice_names_the_content_and_counts_picks()
    {
        var sheet = Sheet(Warden(1));

        AssertPlain(Message(sheet, "choice.unresolved"), ["Fixture Warden", "0 picked"], "warden-skills");
    }

    [Fact]
    public void A_pick_saved_for_a_choice_that_is_not_offered_names_the_content()
    {
        var path = new ChoiceSelection(Fixtures.Warden, "warden-path", [Fixtures.PathOfThorns]);
        var sheet = Sheet(Warden(2, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival), path));

        AssertPlain(Message(sheet, "choice.orphaned"), ["Fixture Warden", "is not active or does not offer that choice"], "warden-path");
    }

    [Fact]
    public void An_orphaned_pick_for_content_that_cannot_be_found_says_so_without_an_id()
    {
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival), new ChoiceSelection(gone, "lost-choice", [])));

        AssertPlain(Message(sheet, "choice.orphaned"), ["content that is not active"], "lost-choice", gone.ContentId.ToString());
    }

    [Fact]
    public void A_choice_with_a_count_below_one_names_the_content_not_the_choice_id()
    {
        var offering = ChoiceTests.Homebrew(22, ContentKind.Feat,
            new ChoiceEffect { Id = "pick-id", ChoiceId = "pick-choice", Count = 0, Options = [Fixtures.WardenAthletics] });
        var character = Fixtures.Load("srd521-ash-m1.json") with { Pins = [offering.Reference] };

        var sheet = ChoiceTests.SheetWith(character, offering);

        AssertPlain(Message(sheet, "choice.invalid-count"), ["Fixture Homebrew 22", "choice(s) it cannot offer"], "pick-id", "pick-choice");
    }

    [Fact]
    public void Missing_content_says_a_version_is_not_available_without_ids()
    {
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var sheet = Sheet(Fixtures.Load("srd521-ash-m1.json") with { Pins = [gone] });

        AssertPlain(Message(sheet, "content.missing"), ["A version of this content is not available"], gone.ContentId.ToString(), gone.RevisionId.ToString());
    }

    [Fact]
    public void An_unsupported_file_version_is_called_a_file_version()
    {
        var future = ChoiceTests.Homebrew(23, ContentKind.Feat) with { SchemaVersion = ContentRevision.CurrentSchemaVersion + 1 };
        var sheet = ChoiceTests.SheetWith(Fixtures.Load("srd521-ash-m1.json") with { Pins = [future.Reference] }, future);

        AssertPlain(Message(sheet, "content.schema-unsupported"), ["Fixture Homebrew 23", $"uses file version {future.SchemaVersion}", $"reads up to file version {ContentRevision.CurrentSchemaVersion}"]);
    }

    [Fact]
    public void A_draft_is_called_a_version_not_a_revision()
    {
        var draft = ChoiceTests.Homebrew(24, ContentKind.Feat) with { Status = RevisionStatus.Draft };
        var sheet = ChoiceTests.SheetWith(Fixtures.Load("srd521-ash-m1.json") with { Pins = [draft.Reference] }, draft);

        AssertPlain(Message(sheet, "content.unpublished"), ["Fixture Homebrew 24", "is a draft version"]);
    }

    [Fact]
    public void A_rules_mismatch_names_both_sets_of_rules_by_their_labels()
    {
        var wrong = Fixtures.Load("srd521-ash-m1.json") with { Pins = [Fixtures.Wayfarer, Fixtures.KeenSenses2014] };

        AssertPlain(Message(Sheet(wrong), "content.rules-family-mismatch"), [NameOf(Fixtures.KeenSenses2014), "written for the 2014 rules", "not the 2024 rules"]);
    }

    [Fact]
    public void A_recorded_exception_says_which_rules_it_follows()
    {
        var rook = Sheet(Fixtures.Load("srd521-rook-exception.json"));

        AssertPlain(Message(rook, "content.cross-family-exception"), [NameOf(Fixtures.KeenSenses2014), "the 2014 rules", "the 2024 rules"]);
    }

    [Fact]
    public void An_unused_exception_names_the_content_it_was_saved_for()
    {
        var unused = Fixtures.Load("srd521-rook-exception.json") with { Pins = [Fixtures.Wayfarer] };

        AssertPlain(Message(Sheet(unused), "character.exception-unused"), [NameOf(Fixtures.KeenSenses2014), "does not use"]);
    }

    [Fact]
    public void A_duplicate_choice_is_reported_without_the_choice_id()
    {
        var problems = Warden(1, Skills(Fixtures.WardenAthletics), Skills(Fixtures.WardenSurvival)).Validate();

        AssertPlain(Assert.Single(problems, d => d.Code == "character.choice-duplicate").Message, ["saved more than once"], "warden-skills");
    }

    [Fact]
    public void An_unknown_rules_id_is_kept_in_the_message_but_the_wording_is_plain()
    {
        var problems = (Fixtures.Load("srd521-ash-m1.json") with { RulesFamily = "homebrew-x" }).Validate();

        var message = Assert.Single(problems, d => d.Code == "character.rules-family-unknown").Message;
        AssertPlain(message, ["'homebrew-x' is not a set of rules this app knows"]);
    }

    private static CharacterSheet Pinned(ContentRevision revision, string? family = null)
    {
        var ash = Fixtures.Load("srd521-ash-m1.json");
        return ChoiceTests.SheetWith(ash with { Pins = [revision.Reference], RulesFamily = family ?? ash.RulesFamily }, revision);
    }

    [Fact]
    public void An_unknown_target_names_the_field_not_the_effect()
    {
        var odd = ChoiceTests.Homebrew(25, ContentKind.Feat, new ModifierEffect { Id = "odd-id", Operation = ModifierOperation.Bonus, Target = "swim-fast", Value = "1" });

        AssertPlain(Message(Pinned(odd), "effect.unknown-target"), ["Fixture Homebrew 25", "changes 'swim-fast'", "not something the sheet calculates"], "odd-id");
    }

    [Fact]
    public void A_field_content_may_not_change_yet_names_the_field_not_the_effect()
    {
        var quick = ChoiceTests.Homebrew(26, ContentKind.Feat, new ModifierEffect { Id = "fast-id", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" }) with { SchemaVersion = 9 };

        AssertPlain(Message(Pinned(quick), "effect.character-only-field"), ["Fixture Homebrew 26", "changes 'Speed'", "may not change yet"], "fast-id", $"'{FieldIds.Speed}'");
    }

    [Fact]
    public void A_missing_stack_group_is_reported_without_the_effect_id()
    {
        var odd = ChoiceTests.Homebrew(27, ContentKind.Feat,
            new ModifierEffect { Id = "stack-id", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1", Stacking = StackingRule.HighestInGroup });

        var warning = Assert.Single(Pinned(odd).Field(FieldIds.Initiative).Warnings, w => w.Code == "effect.stack-group-missing");
        AssertPlain(warning.Message, ["Fixture Homebrew 27", "has an effect that uses highest-in-group stacking"], "stack-id");
    }

    [Fact]
    public void A_toggle_of_other_content_that_is_not_active_is_reported_without_ids()
    {
        var other = Guid.NewGuid();
        var follower = ChoiceTests.Homebrew(28, ContentKind.Feat,
            new ModifierEffect { Id = "follow-id", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1", Timing = EffectTiming.WhileActive, Toggle = "stance-id", ToggleContent = other });

        AssertPlain(Message(Pinned(follower), "modifier.toggle-content-missing"), ["Fixture Homebrew 28", "follows a toggle of other content"], "follow-id", "stance-id", other.ToString());
    }

    [Fact]
    public void A_toggle_whose_group_is_absent_is_named_by_its_label()
    {
        var toggle = ChoiceTests.Homebrew(29, ContentKind.Feat,
            new ToggleEffect { Id = "tg-id", ToggleId = "tg-toggle", Label = "Stone shape", Group = "shapes-id", GroupContent = Guid.NewGuid() });

        AssertPlain(Message(Pinned(toggle), "toggle.group-absent"), ["'Stone shape' is in a group", "it stands alone"], "tg-id", "tg-toggle", "shapes-id");
    }

    [Fact]
    public void A_feat_a_background_may_not_bring_names_the_rules_by_their_label()
    {
        var background = ChoiceTests.Homebrew(30, ContentKind.Background, new ChoiceEffect { Id = "feat", ChoiceId = "feat", Options = [Fixtures.Watchful] });
        var ash = Fixtures.Load("srd521-ash-m1.json") with { RulesFamily = RulesFamilies.Srd51, Pins = [background.Reference], Choices = [new(background.Reference, "feat", [Fixtures.Watchful])] };

        AssertPlain(Message(ChoiceTests.SheetWith(ash, background), "policy.background-feat"), ["Fixture Homebrew 30", "under the 2014 rules"], "SRD 5.1");
    }

    [Fact]
    public void Content_written_for_an_unknown_rules_id_keeps_the_id_in_the_message()
    {
        var odd = ChoiceTests.Homebrew(31, ContentKind.Feat) with { RulesFamilies = ["srd-9"] };

        var message = Message(Pinned(odd), "content.rules-family-mismatch");

        AssertPlainKeeping(message, ["srd-9"], ["Fixture Homebrew 31", "'srd-9' (rules this app does not know)", "not the 2024 rules"]);
        Assert.DoesNotContain("the unknown rules", message, StringComparison.Ordinal);
        Assert.DoesNotContain("the 'srd-9'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_written_for_known_and_unknown_rules_reads_as_a_list()
    {
        var odd = ChoiceTests.Homebrew(32, ContentKind.Feat) with { RulesFamilies = ["srd-9", RulesFamilies.Srd51] };

        var message = Message(Pinned(odd, RulesFamilies.Srd521), "content.rules-family-mismatch");

        AssertPlainKeeping(message, ["srd-9"], ["written for 'srd-9' (rules this app does not know) and the 2014 rules, not the 2024 rules"]);
    }

    [Fact]
    public void An_exception_that_is_not_used_reads_as_an_exception_to_the_rules()
    {
        var unused = Fixtures.Load("srd521-rook-exception.json") with { Pins = [Fixtures.Wayfarer] };

        AssertPlain(Message(Sheet(unused), "character.exception-unused"), ["An exception to the rules is saved for", "but this character does not use it"], "other-rules");
    }

    [Fact]
    public void An_unused_exception_for_missing_content_says_so_without_quoting_a_placeholder()
    {
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var unused = Fixtures.Load("srd521-rook-exception.json") with { Pins = [Fixtures.Wayfarer], CrossFamilyExceptions = [new CrossFamilyException(gone, "Table ruling")] };

        var message = Message(Sheet(unused), "character.exception-unused");
        Assert.Equal("An exception to the rules is saved for content that is not available, but this character does not use it.", message);
        AssertPlain(message, [], gone.ContentId.ToString(), gone.RevisionId.ToString());
    }

    [Fact]
    public void An_exception_without_a_reason_says_what_it_is()
    {
        var rook = Fixtures.Load("srd521-rook-exception.json");

        var problems = (rook with { CrossFamilyExceptions = [new(Fixtures.KeenSenses2014, " ")] }).Validate();

        AssertPlain(Assert.Single(problems, d => d.Code == "character.exception-reason-required").Message, ["An exception to the rules needs a reason"], "cross-family");
    }

    [Fact]
    public void An_orphaned_pick_for_unknown_content_is_one_plain_sentence_without_quotes()
    {
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var sheet = Sheet(Warden(1, Skills(Fixtures.WardenAthletics, Fixtures.WardenSurvival), new ChoiceSelection(gone, "lost-choice", [])));

        Assert.Equal("A pick is saved for content that is not active; it is not applied.", Message(sheet, "choice.orphaned"));
    }

    [Fact]
    public void A_stored_class_column_that_is_not_valid_is_reported_without_the_effect_id()
    {
        var chronicler = CustomClassTests.ChroniclerRevision();
        var hostile = chronicler with
        {
            Effects = [.. chronicler.Effects.Select(e => e is ScaleEffect { ScaleId: "ink" } s ? s with { Values = [.. Enumerable.Repeat(int.MaxValue, 20)] } : e)],
        };
        var pack = Fixtures.ChroniclerPack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [hostile, .. pack.Revisions.Skip(1)]);

        var sheet = CharacterCalculator.Calculate(CustomClassTests.Chronicler(RulesFamilies.Srd521, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);

        AssertPlain(Message(sheet, "scale.invalid"), [hostile.Name, "has a class-table column that needs a name"], "chronicler-ink-column");
    }

    [Fact]
    public void A_repeated_class_column_names_the_column_and_both_entries()
    {
        var pack = Fixtures.ChroniclerPack();
        var echoes = pack.Revisions.Single(r => r.Reference == Fixtures.ArchiveOfEchoes);
        var clash = echoes with { Effects = [.. echoes.Effects.Select(e => e is ScaleEffect s ? s with { ScaleId = "ink", Values = [.. Enumerable.Repeat(99, 20)] } : e)] };
        var catalog = new InMemoryContentCatalog([.. pack.Sources, .. Fixtures.M1Pack().Sources], [.. pack.Revisions.Where(r => r.Reference != Fixtures.ArchiveOfEchoes), clash]);

        var sheet = CharacterCalculator.Calculate(CustomClassTests.Chronicler(RulesFamilies.Srd521, classes: new ClassLevel(Fixtures.Chronicler, 5)), catalog);

        AssertPlain(Message(sheet, "scale.duplicate"), [echoes.Name, "defines scale 'ink'", CustomClassTests.ChroniclerRevision().Name]);
    }

    [Fact]
    public void A_grant_that_names_no_content_is_reported_without_the_grant_id()
    {
        var empty = ChoiceTests.Homebrew(34, ContentKind.Feat, new GrantEffect { Id = "empty-grant-id", Grant = GrantKind.Content });

        AssertPlain(Message(Pinned(empty), "effect.grant-content-missing"), ["Fixture Homebrew 34", "has a grant that names no content"], "empty-grant-id");
    }

    [Fact]
    public void A_nested_grant_is_reported_without_the_grant_id()
    {
        var catalog = Fixtures.M1Catalog();
        var watchful = catalog.FindRevision(Fixtures.Watchful)!;
        var chaining = watchful with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000ff"),
            Effects = [.. watchful.Effects, new GrantEffect { Id = "chain", Grant = GrantKind.Content, Content = Fixtures.Wayfarer }],
        };
        var granter = catalog.FindRevision(Fixtures.Wayfarer)! with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000fe"),
            Effects = [new GrantEffect { Id = "grant", Grant = GrantKind.Content, Content = chaining.Reference }],
        };
        var pack = Fixtures.M1Pack();
        var withChain = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. pack.Sources], [.. Fixtures.Pack().Revisions, .. pack.Revisions, chaining, granter]);
        var character = Fixtures.Load("srd521-ash-m1.json") with { Pins = [granter.Reference] };

        var sheet = CharacterCalculator.Calculate(character, withChain);

        AssertPlain(Message(sheet, "grant.nested-ignored"), [watchful.Name, "was granted by", "one level deep"], "chain");
    }

    [Fact]
    public void A_character_file_from_a_newer_build_speaks_of_file_versions()
    {
        var problems = (Fixtures.Load("srd521-ash-m1.json") with { SchemaVersion = Character.CurrentSchemaVersion + 1 }).Validate();

        AssertPlain(Assert.Single(problems, d => d.Code == "character.schema-unsupported").Message, [$"file version {Character.CurrentSchemaVersion + 1}"]);
    }
}
