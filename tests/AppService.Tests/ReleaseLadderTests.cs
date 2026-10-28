using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Down-conversion export, lane A: the release ladder (D83) and the "opens in" lookup.</summary>
public class ReleaseLadderTests
{
    [Fact]
    public void Rows_are_ascending_and_match_the_spec_table()
    {
        var rows = ReleaseLadder.Rows;

        Assert.Equal([new Version(0, 4, 0), new Version(0, 5, 0), new Version(0, 6, 0), new Version(0, 7, 0)], rows.Select(r => r.Release));
        Assert.Equal([(7, 9, 1, 9, 9), (8, 9, 2, 9, 10), (9, 10, 2, 9, 11), (10, 11, 2, 9, 11)],
            rows.Select(r => (r.CharacterSchema, r.ContentSchema, r.GapNoteSchema, r.FormatVersion, r.DatabaseSchema)));
        Assert.Equal([false, true, true, false], rows.Select(r => r.OfferedAsTarget));
    }

    [Fact]
    public void CurrentRow_is_the_row_of_the_build_version()
    {
        Assert.Equal(new Version(AppVersionStamp.CurrentVersion).Minor, ReleaseLadder.CurrentRow.Release.Minor);
    }

    [Fact]
    public void PreviousMinor_is_the_row_before_current()
    {
        Assert.Equal(new Version(0, 6, 0), ReleaseLadder.PreviousMinor?.Release);
    }

    [Fact]
    public void Constants_never_fall_below_the_current_row()
    {
        var row = ReleaseLadder.CurrentRow;

        Assert.True(row.CharacterSchema <= Character.CurrentSchemaVersion);
        Assert.True(row.ContentSchema <= ContentRevision.CurrentSchemaVersion);
        Assert.True(row.GapNoteSchema <= GapNote.CurrentSchemaVersion);
        Assert.Equal(1, Campaign.CurrentSchemaVersion); // no ladder column: campaign data has been v1 in every release
        Assert.True(row.FormatVersion <= PackageManifest.CurrentFormatVersion);
        Assert.True(row.DatabaseSchema <= SqliteStore.LatestSchemaVersion);
    }

    [Fact]
    public void Bundled_sets_are_subsets_of_the_catalog()
    {
        var catalog = TomeStackApp.BundledRevisionIds;

        Assert.All(ReleaseLadder.Rows, row =>
        {
            Assert.NotEmpty(row.BundledRevisionIds);
            Assert.True(row.BundledRevisionIds.IsSubsetOf(catalog), $"{row.Release} has a bundled revision the current packs lack");
        });
    }

    private static readonly Guid FixtureSource = Guid.Parse("7c000000-0000-4000-8000-000000000001");

    private static Character Hero(string name = "Compat Hero") =>
        new() { Id = Guid.NewGuid(), Name = name, RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10) };

    private static Character V9(Character c) => c with { Xp = 100 };

    private static Character V10(Character c) => c with { Play = (c.Play ?? new PlayState()) with { Encounter = new EncounterState(0) } };

    private static ContentRevision Revision(string name, int schemaVersion, Guid? revisionId = null) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = revisionId ?? Guid.NewGuid(),
        Kind = ContentKind.Feature,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(FixtureSource, new PageRef(1)),
        Status = RevisionStatus.Published,
        SchemaVersion = schemaVersion,
    };

    private static LadderRow Row(string release) => ReleaseLadder.Rows.Single(r => r.Release.ToString(3) == release);

    [Fact]
    public void OpensIn_is_0_5_0_for_a_v8_character_and_v9_content()
    {
        Assert.Equal(Row("0.5.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 9)], [Hero()], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_6_0_for_v9_character()
    {
        Assert.Equal(Row("0.6.0"), ReleaseLadder.OpensIn([], [V9(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_7_0_for_v11_content_or_v10_character()
    {
        Assert.Equal(Row("0.7.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 11)], [Hero()], 0, []));
        Assert.Equal(Row("0.7.0"), ReleaseLadder.OpensIn([], [V10(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_null_above_every_row()
    {
        Assert.Null(ReleaseLadder.OpensIn([Revision("Compat Tide Stance", ContentRevision.CurrentSchemaVersion + 1)], [Hero()], 0, []));
        Assert.Null(ReleaseLadder.OpensIn([], [Hero() with { SchemaVersion = Character.CurrentSchemaVersion + 1 }], 0, []));
        Assert.Null(ReleaseLadder.OpensIn([], [], GapNote.CurrentSchemaVersion + 1, []));
    }

    [Fact]
    public void OpensIn_skips_rows_lacking_a_bundled_revision()
    {
        Assert.Null(ReleaseLadder.OpensIn([], [Hero()], 0, [Guid.Parse("7c00e000-0000-4000-8000-0000000000ff")]));
        var inEverySet = ReleaseLadder.Rows[0].BundledRevisionIds.First(id => ReleaseLadder.Rows.All(r => r.BundledRevisionIds.Contains(id)));
        Assert.Equal(Row("0.5.0"), ReleaseLadder.OpensIn([], [Hero()], 0, [inEverySet])); // a v8 character; the id changes nothing
        Assert.Equal(Row("0.4.0"), ReleaseLadder.OpensIn([], [], 1, []));
    }

    [Fact]
    public void Label_names_the_release_or_a_newer_build()
    {
        Assert.Equal("0.6.0", ReleaseLadder.Label(Row("0.6.0")));
        Assert.Equal("a newer TomeStack", ReleaseLadder.Label(null));
    }

    [Fact]
    public void Evaluate_warns_only_above_previous_minor()
    {
        var quiet = NewerBuild.Evaluate([Revision("Compat Tide Stance", 10)], [V9(Hero())], 0, [], _ => "");
        Assert.Equal("0.6.0", quiet.OpensIn);
        Assert.Empty(quiet.Warnings);
        Assert.Empty(quiet.Causes);

        var hero = V10(Hero());
        var one = NewerBuild.Evaluate([], [hero], 0, [], _ => "");
        Assert.Equal("0.7.0", one.OpensIn);
        var warning = Assert.Single(one.Warnings);
        Assert.Equal(NewerBuild.Code, warning.Code);
        Assert.Equal($"Opening this needs TomeStack 0.7.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Compat Hero' is a character at schema 10. Older versions refuse the file.", warning.Message);
        var cause = Assert.Single(one.Causes);
        Assert.Equal(new OpensInCause($"characters/{hero.Id:D}.json", "character", hero.Id, "Compat Hero", 10, null, "'Compat Hero' is a character at schema 10"), cause);
    }

    [Fact]
    public void Evaluate_joins_causes_in_the_order_characters_content_gap_notes()
    {
        var hero = V10(Hero());
        var stance = Revision("Compat Tide Stance", 11);

        var result = NewerBuild.Evaluate([stance], [hero], 0, [], _ => "");

        Assert.Equal($"Opening this needs TomeStack 0.7.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Compat Hero' is a character at schema 10, and 'Compat Tide Stance' is content at schema 11. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
        Assert.Equal(["character", "content"], result.Causes.Select(c => c.Kind));
        Assert.Equal($"content/{stance.RevisionId:D}.json", result.Causes[1].Entry);

        var gap = NewerBuild.Evaluate([], [], ReleaseLadder.PreviousMinor!.GapNoteSchema + 1, [], _ => "");
        var gapCause = Assert.Single(gap.Causes);
        Assert.Equal(new OpensInCause("gaps/", "gapNote", Guid.Empty, "gap notes", 3, null, "the gap notes are at schema 3"), gapCause);
    }

    [Fact]
    public void Evaluate_names_a_bundled_revision_the_previous_minor_lacks()
    {
        var hero = Hero();
        var species = Revision("Example Species", 3);
        var heroWithSpecies = hero with { Pins = [species.Reference] };

        var result = NewerBuild.Evaluate([species], [heroWithSpecies], 0, [species.RevisionId], _ => "SRD 5.2.1");

        var cause = Assert.Single(result.Causes);
        Assert.Equal(new OpensInCause($"content/{species.RevisionId:D}.json", "content", species.RevisionId, "Example Species", null, "SRD 5.2.1",
            "'Compat Hero' uses 'Example Species' from the bundled SRD 5.2.1 pack, which TomeStack 0.6.0 does not have"), cause);
        Assert.Null(result.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version newer than 0.7.0: it holds bundled SRD content that released versions do not have.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_caps_the_warning_at_three_distinct_messages()
    {
        var heroes = new[] { "Ann", "Bo", "Cy", "Di", "Di" }.Select(n => V10(Hero(n))).ToList();

        var result = NewerBuild.Evaluate([], heroes, 0, [], _ => "");

        Assert.Equal(5, result.Causes.Count);
        Assert.Equal($"Opening this needs TomeStack 0.7.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Ann' is a character at schema 10, 'Bo' is a character at schema 10, 'Cy' is a character at schema 10, and 1 more. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_keeps_the_joined_form_for_exactly_three_messages()
    {
        var heroes = new[] { "Ann", "Bo", "Cy" }.Select(n => V10(Hero(n))).ToList();

        var result = NewerBuild.Evaluate([], heroes, 0, [], _ => "");

        Assert.Equal($"Opening this needs TomeStack 0.7.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Ann' is a character at schema 10, and 'Bo' is a character at schema 10, and 'Cy' is a character at schema 10. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_does_not_borrow_a_character_for_an_unpinned_bundled_revision()
    {
        var species = Revision("Example Species", 3);

        var result = NewerBuild.Evaluate([species], [Hero("One"), Hero("Two")], 0, [species.RevisionId], _ => "SRD 5.2.1");

        Assert.Equal("'Example Species' is from the bundled SRD 5.2.1 pack, which TomeStack 0.6.0 does not have", Assert.Single(result.Causes).Message);
    }

    [Fact]
    public void Evaluate_points_an_uncarried_bundled_cause_at_the_pinning_character()
    {
        var species = Revision("Example Species", 3);
        var hero = Hero() with { Pins = [species.Reference] };

        var result = NewerBuild.Evaluate([species], [Hero("Other"), hero], 0, [species.RevisionId], _ => "SRD 5.2.1", bundledCarried: false);

        Assert.Equal($"characters/{hero.Id:D}.json", Assert.Single(result.Causes).Entry);
    }

    [Fact]
    public void CurrentRowOrLast_falls_back_to_the_last_row_for_a_version_with_no_row()
    {
        Assert.Equal(ReleaseLadder.Rows[^1], ReleaseLadder.CurrentRowOrLast("0.9.0"));
        Assert.Equal(ReleaseLadder.Rows[^2], ReleaseLadder.PreviousMinorFor("0.9.0"));
        Assert.Equal(Row("0.6.0"), ReleaseLadder.CurrentRowOrLast("0.6.2"));
    }

    [Fact]
    public void Evaluate_gives_a_bundled_revision_that_is_also_new_content_one_cause()
    {
        var revision = Revision("Example Species", 11);

        var result = NewerBuild.Evaluate([revision], [Hero()], 0, [revision.RevisionId], _ => "SRD 5.2.1");

        var cause = Assert.Single(result.Causes);
        Assert.Equal(11, cause.SchemaVersion);
        Assert.Equal($"content/{revision.RevisionId:D}.json", cause.Entry);
    }

    [Fact]
    public void Evaluate_uses_the_gap_note_sentence_when_only_the_gap_notes_are_too_new()
    {
        var result = NewerBuild.Evaluate([], [], 3, [], _ => "");

        Assert.Null(result.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version that reads gap note schema 3 or later: it holds gap notes that older versions refuse.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_uses_the_unreleased_sentence_when_opensIn_is_null()
    {
        var content = NewerBuild.Evaluate([Revision("Compat Tide Stance", 12)], [], 0, [], _ => "");
        Assert.Null(content.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version that reads content schema 12 or later: it holds content that older versions refuse.", Assert.Single(content.Warnings).Message);

        var character = NewerBuild.Evaluate([], [Hero() with { SchemaVersion = 11 }], 0, [], _ => "");
        Assert.Equal("Opening this needs a TomeStack version that reads character schema 11 or later: it holds a character that older versions refuse.", Assert.Single(character.Warnings).Message);
    }

    [Theory]
    [InlineData("0.6.0", true)]
    [InlineData("0.5.0", true)]
    [InlineData("0.4.0", false)]
    [InlineData("0.7.0", false)]
    [InlineData("0.7.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("0.6", false)]
    public void Target_accepts_offered_rows_only(string? release, bool offered)
    {
        var target = ReleaseLadder.Target(release);

        Assert.Equal(offered, target is not null);
        if (offered) Assert.Equal(release, target!.Release.ToString(3));
    }
}
