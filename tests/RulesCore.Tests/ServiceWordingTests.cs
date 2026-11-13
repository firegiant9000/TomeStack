using System.Text.RegularExpressions;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Wave 3, Task 2b (D99): the messages a player reads on the sheet use plain words, name content by its display name and never
/// print a GUID or an author id. <see cref="AssertPlain"/> is the shared check; later tasks extend this class.
/// </summary>
public partial class ServiceWordingTests
{
    private static readonly string[] BannedWords = ["revision", "rules family", "schema", "srd-5", "package", "candidate"];

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    /// <summary>The reusable wording check: no GUID, none of the banned words, none of <paramref name="alsoNot"/> (ids), and every <paramref name="names"/> present.</summary>
    internal static void AssertPlain(string message, string[] names, params string[] alsoNot)
    {
        Assert.DoesNotMatch(GuidPattern(), message);
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

    [Fact]
    public void A_character_file_from_a_newer_build_speaks_of_file_versions()
    {
        var problems = (Fixtures.Load("srd521-ash-m1.json") with { SchemaVersion = Character.CurrentSchemaVersion + 1 }).Validate();

        AssertPlain(Assert.Single(problems, d => d.Code == "character.schema-unsupported").Message, [$"file version {Character.CurrentSchemaVersion + 1}"]);
    }
}
